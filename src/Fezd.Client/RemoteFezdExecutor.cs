using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using Fezd.Contracts;

namespace Fezd.Client
{
    /// <summary>
    /// <see cref="IFezdExecutor"/> that runs operations against a remote gateway
    /// over pinned TLS with a scoped bearer token. Uploads the project, enqueues the
    /// job, streams its logs, downloads artifacts, and maps the job's exit code back
    /// so a remote run reproduces a local one. Comms failures become
    /// <see cref="RemoteCommsException"/> with an actionable message + exit code.
    /// AOT/trim-safe: all JSON goes through the source-generated context.
    /// </summary>
    public sealed class RemoteFezdExecutor : IFezdExecutor, IDisposable
    {
        private readonly RemoteOptions _opts;
        private readonly HttpClient _http;
        private readonly ProxyRouteInfo _proxyRoute;
        private string _certFailure;

        public RemoteFezdExecutor(RemoteOptions options)
        {
            _opts = options ?? throw new ArgumentNullException(nameof(options));
            if (_opts.BaseUrl == null)
                throw new RemoteCommsException("No remote endpoint set. Pass --remote <https url>.",
                    FezdExitCodes.UsageError);

            IWebProxy systemProxy = _opts.NoProxy ? null : WebRequest.DefaultWebProxy;
            _proxyRoute = ProxyRouteDetector.Detect(_opts.BaseUrl, _opts.NoProxy, systemProxy);

#if NETFRAMEWORK
            // net48 defaults can exclude TLS 1.2; the gateway requires it.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { /* ignore */ }
#endif

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (req, cert, chain, errors) =>
                    ValidateServerCert(cert, chain, errors),
                // Large .zef uploads behind corp proxies often mishandle 100-continue.
                AllowAutoRedirect = true
            };
            if (_opts.NoProxy)
            {
                handler.UseProxy = false;
                handler.Proxy = null;
            }
            else if (systemProxy != null)
            {
                // Use the same system proxy instance whose bypass/PAC decision
                // selected the route above.
                handler.Proxy = systemProxy;
            }

            _http = new HttpClient(handler)
            {
                BaseAddress = _opts.BaseUrl,
                Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds <= 0 ? 300 : _opts.TimeoutSeconds)
            };
            _http.DefaultRequestHeaders.ExpectContinue = false;
        }

        // ---- IFezdExecutor ----

        public DoctorReportDto Doctor(DoctorOptionsDto options)
        {
            options = options ?? new DoctorOptionsDto();
            var q = new StringBuilder("/api/v1/doctor?");
            q.Append("simulator=").Append(options.Simulator ? "true" : "false");
            if (!string.IsNullOrEmpty(options.TargetAddress))
                q.Append("&target=").Append(Uri.EscapeDataString(options.TargetAddress));
            q.Append("&port=").Append(options.Port);
            q.Append("&timeout=").Append(options.ConnectTimeoutMs);
            if (!string.IsNullOrEmpty(options.TestProjectPath))
                q.Append("&test-project=").Append(Uri.EscapeDataString(options.TestProjectPath));
            q.Append("&deep=").Append(options.Deep ? "true" : "false");

            using (HttpResponseMessage resp = Send(HttpMethod.Get, q.ToString(), null, auth: true))
                return ReadJson(resp, FezdJsonContext.Default.DoctorReportDto);
        }

        public JobResultDto Build(BuildRequestDto request)
        {
            // Forward the full request; only substitute the uploaded project handle.
            request.ProjectId = UploadProject(request.ZefPath);
            request.SaveStu = request.SaveStu || !string.IsNullOrEmpty(request.OutputStuPath);
            JobStatusDto job = PostJob("/api/v1/build",
                JsonSerializer.Serialize(request, FezdJsonContext.Default.BuildRequestDto));
            JobResultDto result = RunJob(job.Id);

            if (result.Success && result.Artifacts.Count > 0)
            {
                string destDir = !string.IsNullOrEmpty(request.OutputStuPath)
                    ? Path.GetDirectoryName(Path.GetFullPath(request.OutputStuPath))
                    : Directory.GetCurrentDirectory();
                foreach (string name in result.Artifacts)
                {
                    string outPath = name.EndsWith(".stu", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(request.OutputStuPath)
                        ? request.OutputStuPath
                        : Path.Combine(destDir ?? ".", name);
                    SaveArtifact(job.Id, name, outPath);
                }
            }
            return result;
        }

        public JobResultDto Deploy(DeployRequestDto request)
        {
            request.ProjectId = UploadProject(request.ZefPath);
            bool simulator = request.Target == TargetKindDto.Simulator;
            var create = new CreateSessionRequestDto
            {
                ProjectId = request.ProjectId,
                Simulator = simulator,
                TargetAddress = simulator ? null : request.TargetAddress,
                Port = request.Port,
                Driver = request.Driver,
                Run = request.Run,
                Force = request.Force,
                ReturnStu = request.SaveStu,
                SaveSta = request.SaveSta,
                BuildBeforeDeploy = request.BuildBeforeDeploy,
                AppPassword = request.AppPassword,
                AppPasswordOld = request.AppPasswordOld,
                ReservationName = request.ReservationName
            };

            SessionStatusDto session;
            using (var content = new StringContent(
                JsonSerializer.Serialize(create, FezdJsonContext.Default.CreateSessionRequestDto),
                Encoding.UTF8, "application/json"))
            using (HttpResponseMessage resp = Send(HttpMethod.Post, "/api/v1/sessions", content, auth: true))
            {
                session = ReadJson(resp, FezdJsonContext.Default.SessionStatusDto);
                _opts.Write("info",
                    $"Session {session.Id} accepted (queue position {session.QueuePosition}, depth {session.QueueDepth}).");
            }

            JobResultDto result = FollowSession(session.Id);
            DownloadSessionArtifacts(session.Id, result, request.OutputDir);
            return result;
        }

        /// <summary>
        /// Request cancel for a queued or running deploy session
        /// (<c>POST /api/v1/sessions/{id}/cancel</c>).
        /// </summary>
        public SessionStatusDto CancelSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new RemoteCommsException("Session id is required.", FezdExitCodes.UsageError);

            using (HttpResponseMessage resp = Send(HttpMethod.Post,
                $"/api/v1/sessions/{Uri.EscapeDataString(sessionId.Trim())}/cancel",
                new StringContent(string.Empty), auth: true))
            {
                return ReadJson(resp, FezdJsonContext.Default.SessionStatusDto);
            }
        }

        private JobResultDto FollowSession(string sessionId)
        {
            long cursor = 0;
            JobResultDto completedFromEvent = null;
            while (true)
            {
                // Prefer poll of /events?after= (works everywhere; WS is optional upgrade).
                using (HttpResponseMessage resp = Send(HttpMethod.Get,
                    $"/api/v1/sessions/{sessionId}/events?after={cursor}", null, auth: true))
                {
                    string body = ReadBody(resp);
                    EnsureSuccess(resp, body);
                    using (JsonDocument doc = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body))
                    {
                        JsonElement root = doc.RootElement;
                        if (root.TryGetProperty("entries", out JsonElement entries) &&
                            entries.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement e in entries.EnumerateArray())
                            {
                                string type = e.TryGetProperty("type", out JsonElement t) ? t.GetString() : null;
                                string level = e.TryGetProperty("level", out JsonElement lv) ? lv.GetString() : "info";
                                string message = e.TryGetProperty("message", out JsonElement m) ? m.GetString() : null;
                                if (string.Equals(type, "log.line", StringComparison.OrdinalIgnoreCase) &&
                                    !string.IsNullOrEmpty(message))
                                    _opts.Write(level ?? "info", message);
                                else if (string.Equals(type, "queue.updated", StringComparison.OrdinalIgnoreCase) &&
                                         !string.IsNullOrEmpty(message))
                                    _opts.Write("info", message);
                                else if (string.Equals(type, "phase.changed", StringComparison.OrdinalIgnoreCase) &&
                                         !string.IsNullOrEmpty(message))
                                    _opts.Write("info", message);
                                else if (string.Equals(type, "session.completed", StringComparison.OrdinalIgnoreCase))
                                {
                                    string phase = e.TryGetProperty("phase", out JsonElement ph)
                                        ? ph.GetString() : null;
                                    bool failed = string.Equals(phase, "Failed", StringComparison.OrdinalIgnoreCase) ||
                                                  string.Equals(phase, "Cancelled", StringComparison.OrdinalIgnoreCase);

                                    if (failed)
                                    {
                                        int exitCode = e.TryGetProperty("exitCode", out JsonElement ec) &&
                                                       ec.TryGetInt32(out int code)
                                            ? code
                                            : FezdExitCodes.Error;
                                        string err = string.IsNullOrEmpty(message)
                                            ? "Session " + (phase ?? "failed") + "."
                                            : message;
                                        // Capture for exit; server Error log.line + Finish() surface it.
                                        completedFromEvent = JobResultDto.Fail(exitCode, err);
                                    }
                                    else if (e.TryGetProperty("artifacts", out JsonElement arts) &&
                                             arts.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (JsonElement a in arts.EnumerateArray())
                                        {
                                            string url = a.TryGetProperty("url", out JsonElement u) ? u.GetString() : null;
                                            string name = a.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                                            if (!string.IsNullOrEmpty(url))
                                                _opts.Write("info", "Artifact ready: " + (name ?? url) + " -> " + url);
                                        }
                                    }
                                }
                            }
                        }

                        if (root.TryGetProperty("nextCursor", out JsonElement nc) && nc.TryGetInt64(out long next))
                            cursor = next;

                        bool done = root.TryGetProperty("done", out JsonElement d) && d.ValueKind == JsonValueKind.True;
                        if (done)
                            break;
                    }
                }

                // Terminal failure from session.completed: stop polling immediately.
                if (completedFromEvent != null && !completedFromEvent.Success)
                    break;

                Thread.Sleep(Math.Max(200, _opts.PollIntervalMs));
            }

            using (HttpResponseMessage resp = Send(HttpMethod.Get, $"/api/v1/sessions/{sessionId}", null, auth: true))
            {
                SessionStatusDto status = ReadJson(resp, FezdJsonContext.Default.SessionStatusDto);
                JobResultDto result = status.Result
                    ?? completedFromEvent
                    ?? JobResultDto.Fail(FezdExitCodes.Error, "Session finished without a result.");

                if (result.Success && status.Artifacts != null)
                {
                    foreach (ArtifactRefDto a in status.Artifacts)
                    {
                        if (!string.IsNullOrEmpty(a.Url))
                            _opts.Write("info", "Download: " + a.Url);
                    }
                }
                return result;
            }
        }

        private void DownloadSessionArtifacts(string sessionId, JobResultDto result, string destDir)
        {
            if (!result.Success || result.Artifacts == null || result.Artifacts.Count == 0)
                return;
            string dir = string.IsNullOrEmpty(destDir) ? Directory.GetCurrentDirectory() : destDir;
            foreach (string name in result.Artifacts)
                SaveSessionArtifact(sessionId, name, Path.Combine(dir, name));
        }

        private void SaveSessionArtifact(string sessionId, string name, string outPath)
        {
            using (HttpResponseMessage resp = Send(HttpMethod.Get,
                $"/api/v1/sessions/{sessionId}/artifacts/{Uri.EscapeDataString(name)}", null, auth: true))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    EnsureSuccess(resp, ReadBody(resp));
                    return;
                }
                byte[] bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                string full = Path.GetFullPath(outPath);
                string parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                File.WriteAllBytes(full, bytes);
                _opts.Write("info", $"Saved artifact -> {full}");
            }
        }

        public JobResultDto Export(ExportRequestDto request)
        {
            request.ProjectId = UploadProject(request.ZefPath);
            JobStatusDto job = PostJob("/api/v1/export",
                JsonSerializer.Serialize(request, FezdJsonContext.Default.ExportRequestDto));
            JobResultDto result = RunJob(job.Id);
            DownloadAll(job.Id, result, request.OutputDir);
            return result;
        }

        /// <summary>GET /api/v1/simulator — status of host-managed sim.exe.</summary>
        public SimulatorStatusDto SimulatorStatus()
        {
            using (HttpResponseMessage resp = Send(HttpMethod.Get, "/api/v1/simulator", null, auth: true))
                return ReadJson(resp, FezdJsonContext.Default.SimulatorStatusDto);
        }

        /// <summary>POST /api/v1/simulator/stop — stop sim.exe (cuts idle wait).</summary>
        public SimulatorStopResultDto SimulatorStop()
        {
            using (HttpResponseMessage resp = Send(HttpMethod.Post, "/api/v1/simulator/stop",
                       new StringContent("{}", Encoding.UTF8, "application/json"), auth: true))
                return ReadJson(resp, FezdJsonContext.Default.SimulatorStopResultDto);
        }

        // ---- diagnostics: fezd ping / remote check ----

        public RemoteCheckResult Check()
        {
            var r = new RemoteCheckResult
            {
                Endpoint = _opts.BaseUrl.ToString(),
                UsesProxy = _proxyRoute.UsesProxy,
                Route = _proxyRoute.Description
            };

            // 1) Raw TCP to the actual first hop: the gateway for a direct route,
            // or the system HTTP proxy when its bypass rules select one.
            try
            {
                Uri firstHop = _proxyRoute.UsesProxy ? _proxyRoute.ProxyUri : _opts.BaseUrl;
                using (var tcp = new TcpClient())
                {
                    if (!tcp.ConnectAsync(firstHop.Host, firstHop.Port)
                            .Wait(Math.Max(1000, _opts.TimeoutSeconds * 100)))
                        throw new TimeoutException("connect timed out");
                    r.TcpOk = tcp.Connected;
                }
            }
            catch (Exception ex)
            {
                r.TcpOk = false;
                r.Detail = (_proxyRoute.UsesProxy ? "Proxy" : "Gateway")
                           + " TCP connect failed (continuing via HTTP): "
                           + Innermost(ex).Message;
            }

            // 2) TLS + system CA / optional pin (unauthenticated healthz via HttpClient).
            try
            {
                using (HttpResponseMessage resp = Send(HttpMethod.Get, "/healthz", null, auth: false))
                {
                    r.TlsOk = true;
                    r.PinOk = true; // legacy field: true when TLS trust succeeded
                }
            }
            catch (RemoteCommsException ex)
            {
                bool tls = ex.Message.IndexOf("pin", StringComparison.OrdinalIgnoreCase) >= 0
                           || ex.Message.IndexOf("cert", StringComparison.OrdinalIgnoreCase) >= 0
                           || ex.Message.IndexOf("TLS", StringComparison.Ordinal) >= 0;
                r.TlsOk = !tls;
                r.PinOk = false;
                r.Detail = string.IsNullOrEmpty(r.Detail) ? ex.Message : r.Detail + "; " + ex.Message;
                return r;
            }

            // 3) Auth + scopes.
            try
            {
                using (HttpResponseMessage resp = Send(HttpMethod.Get, "/api/v1/whoami", null, auth: true))
                {
                    WhoAmIDto who = ReadJson(resp, FezdJsonContext.Default.WhoAmIDto);
                    r.AuthOk = true;
                    r.Scopes = who?.Scopes ?? r.Scopes;
                }
            }
            catch (RemoteCommsException ex)
            {
                r.Detail = ex.Message;
                return r;
            }

            // 4) Server version (best effort).
            try
            {
                using (HttpResponseMessage resp = Send(HttpMethod.Get, "/api/v1/version", null, auth: true))
                    r.ServerVersion = ReadJson(resp, FezdJsonContext.Default.VersionDto)?.Version;
            }
            catch { /* version is informational */ }

            return r;
        }

        // ---- upload / jobs / artifacts ----

        private string UploadProject(string zefPath)
        {
            if (string.IsNullOrEmpty(zefPath) || !File.Exists(zefPath))
                throw new RemoteCommsException($"Project file not found: '{zefPath}'.", FezdExitCodes.UsageError);

            // One automatic full retry after rollback/resend_required or mid-upload failure.
            try
            {
                return UploadProjectOnce(zefPath);
            }
            catch (RemoteCommsException ex) when (IsResendWorthy(ex))
            {
                _opts.Write("warn", "Upload failed; rolling back and retrying with a full resend...");
                return UploadProjectOnce(zefPath);
            }
        }

        private static bool IsResendWorthy(RemoteCommsException ex)
        {
            if (ex == null) return false;
            string msg = ex.Message ?? "";
            return msg.IndexOf("resend", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("Upload failed at part", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("Cannot reach", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string UploadProjectOnce(string zefPath)
        {
            string sha = Sha256File(zefPath);
            string fileName = Path.GetFileName(zefPath);
            long size = new FileInfo(zefPath).Length;
            int chunkKb = _opts.ResolveUploadChunkKb(_proxyRoute.UsesProxy);
            long preferredChunk = (long)chunkKb * 1024;

            if (_opts.NoChunkedUpload || size <= preferredChunk)
                return UploadProjectSingleShot(zefPath, sha, fileName, size);

            return UploadProjectChunked(zefPath, sha, fileName, size, chunkKb);
        }

        private string UploadProjectSingleShot(string zefPath, string sha, string fileName, long size)
        {
            _opts.Write("info", $"Uploading {fileName} ({size} bytes)...");

            using (var fs = new FileStream(zefPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var content = new StreamContent(fs))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using (HttpResponseMessage resp = Send(HttpMethod.Post, "/api/v1/projects", content, auth: true, req =>
                {
                    req.Headers.TryAddWithoutValidation("X-Fezd-Sha256", sha);
                    req.Headers.TryAddWithoutValidation("X-Fezd-Filename", fileName);
                }))
                {
                    return FinishUpload(resp, sha);
                }
            }
        }

        private string UploadProjectChunked(string zefPath, string sha, string fileName, long size, int chunkKb)
        {
            var initReq = new ChunkedUploadInitRequestDto
            {
                FileName = fileName,
                Sha256 = sha,
                Size = size,
                ChunkSizeKb = chunkKb
            };
            string initJson = JsonSerializer.Serialize(initReq, FezdJsonContext.Default.ChunkedUploadInitRequestDto);
            ChunkedUploadInitResultDto init;
            using (var initContent = new StringContent(initJson, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage initResp = Send(HttpMethod.Post, "/api/v1/projects/uploads", initContent, auth: true))
            {
                init = ReadJson(initResp, FezdJsonContext.Default.ChunkedUploadInitResultDto);
            }

            string uploadId = init.UploadId;
            long chunkBytes = init.ChunkSizeBytes > 0 ? init.ChunkSizeBytes : (long)chunkKb * 1024;
            int totalParts = init.TotalParts > 0
                ? init.TotalParts
                : (int)((size + chunkBytes - 1) / chunkBytes);

            _opts.Write("info",
                $"Uploading {fileName} ({size} bytes) in {totalParts} parts of up to {chunkBytes} bytes...");

            try
            {
                using (var fs = new FileStream(zefPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] buffer = new byte[chunkBytes > int.MaxValue ? 1024 * 1024 : (int)chunkBytes];
                    for (int index = 0; index < totalParts; index++)
                    {
                        long offset = index * chunkBytes;
                        int toRead = (int)Math.Min(chunkBytes, size - offset);
                        if (toRead <= 0)
                            break;

                        _opts.Write("info", $"Uploading part {index + 1} of {totalParts}...");

                        fs.Position = offset;
                        int read = 0;
                        while (read < toRead)
                        {
                            int n = fs.Read(buffer, read, toRead - read);
                            if (n == 0) break;
                            read += n;
                        }
                        if (read != toRead)
                        {
                            AbortUpload(uploadId);
                            throw new RemoteCommsException(
                                $"Upload failed at part {index + 1} of {totalParts}: could not read local file.",
                                FezdExitCodes.Error);
                        }

                        using (var content = new ByteArrayContent(buffer, 0, read))
                        {
                            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                            content.Headers.ContentLength = read;
                            string partUrl = $"/api/v1/projects/uploads/{uploadId}/parts/{index}";
                            try
                            {
                                using (HttpResponseMessage partResp = Send(HttpMethod.Put, partUrl, content, auth: true))
                                {
                                    ChunkedUploadPartAckDto ack =
                                        ReadJson(partResp, FezdJsonContext.Default.ChunkedUploadPartAckDto);
                                    _opts.Write("info",
                                        $"Part {index + 1} of {totalParts} accepted " +
                                        $"({ack.ReceivedParts}/{ack.TotalParts} on server).");
                                }
                            }
                            catch (RemoteCommsException ex)
                            {
                                AbortUpload(uploadId);
                                throw new RemoteCommsException(
                                    $"Upload failed at part {index + 1} of {totalParts}: {ex.Message}. " +
                                    "Rolling back; full resend required.",
                                    ex, ex.ExitCode != 0 ? ex.ExitCode : FezdExitCodes.ConnectivityError);
                            }
                        }
                    }
                }

                using (HttpResponseMessage completeResp = Send(
                    HttpMethod.Post, $"/api/v1/projects/uploads/{uploadId}/complete", null, auth: true))
                {
                    return FinishUpload(completeResp, sha);
                }
            }
            catch (RemoteCommsException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AbortUpload(uploadId);
                throw new RemoteCommsException(
                    $"Upload failed: {ex.Message}. Rolling back; full resend required.",
                    ex, FezdExitCodes.ConnectivityError);
            }
        }

        private void AbortUpload(string uploadId)
        {
            if (string.IsNullOrEmpty(uploadId))
                return;
            try
            {
                using (Send(HttpMethod.Delete, $"/api/v1/projects/uploads/{uploadId}", null, auth: true))
                {
                    _opts.Write("debug", $"Aborted upload session {uploadId}.");
                }
            }
            catch (Exception ex)
            {
                _opts.Write("debug", $"Best-effort abort of upload {uploadId} failed: {ex.Message}");
            }
        }

        private string FinishUpload(HttpResponseMessage resp, string localSha)
        {
            ProjectUploadResultDto result = ReadJson(resp, FezdJsonContext.Default.ProjectUploadResultDto);
            if (!string.Equals(result.Sha256, localSha, StringComparison.OrdinalIgnoreCase))
                throw new RemoteCommsException(
                    $"Upload integrity mismatch: local sha256={localSha}, server sha256={result.Sha256}.",
                    FezdExitCodes.Error);
            _opts.Write("info",
                $"Upload verified: sha256={result.Sha256}, {result.Size} bytes (integrity OK).");
            _opts.Write("debug", $"Uploaded project {result.ProjectId} (dedup={result.Deduplicated}).");
            return result.ProjectId;
        }

        private JobStatusDto PostJob(string url, string json)
        {
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage resp = Send(HttpMethod.Post, url, content, auth: true))
            {
                JobStatusDto job = ReadJson(resp, FezdJsonContext.Default.JobStatusDto);
                _opts.Write("info", $"Job {job.Id} ({job.Kind}) accepted.");
                return job;
            }
        }

        private JobResultDto RunJob(string jobId)
        {
            long cursor = 0;
            while (true)
            {
                JobLogsDto logs;
                using (HttpResponseMessage resp = Send(HttpMethod.Get, $"/api/v1/jobs/{jobId}/logs?after={cursor}", null, auth: true))
                    logs = ReadJson(resp, FezdJsonContext.Default.JobLogsDto);

                if (logs.Entries != null)
                    foreach (JobLogEntryDto e in logs.Entries)
                        _opts.Write(e.Level ?? "info", e.Message);

                cursor = logs.NextCursor;
                if (logs.Done)
                    break;
                if (logs.Entries == null || logs.Entries.Count == 0)
                    Thread.Sleep(Math.Max(200, _opts.PollIntervalMs));
            }

            using (HttpResponseMessage resp = Send(HttpMethod.Get, $"/api/v1/jobs/{jobId}", null, auth: true))
            {
                JobStatusDto status = ReadJson(resp, FezdJsonContext.Default.JobStatusDto);
                return status.Result ?? JobResultDto.Fail(FezdExitCodes.Error, "Job finished without a result.");
            }
        }

        private void DownloadAll(string jobId, JobResultDto result, string destDir)
        {
            if (!result.Success || result.Artifacts.Count == 0)
                return;
            string dir = string.IsNullOrEmpty(destDir) ? Directory.GetCurrentDirectory() : destDir;
            foreach (string name in result.Artifacts)
                SaveArtifact(jobId, name, Path.Combine(dir, name));
        }

        private void SaveArtifact(string jobId, string name, string outPath)
        {
            using (HttpResponseMessage resp = Send(HttpMethod.Get, $"/api/v1/jobs/{jobId}/artifacts/{Uri.EscapeDataString(name)}", null, auth: true))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    EnsureSuccess(resp, ReadBody(resp));
                    return;
                }
                byte[] bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                string full = Path.GetFullPath(outPath);
                string parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                File.WriteAllBytes(full, bytes);
                _opts.Write("info", $"Saved artifact -> {full}");
            }
        }

        // ---- HTTP plumbing ----

        private HttpResponseMessage Send(HttpMethod method, string url, HttpContent content, bool auth,
            Action<HttpRequestMessage> customize = null)
        {
            string rid = Guid.NewGuid().ToString("N");
            var req = new HttpRequestMessage(method, url) { Content = content };
            if (auth)
            {
                if (string.IsNullOrEmpty(_opts.Token))
                    throw new RemoteCommsException(
                        "No license/token set. Pass --connection <file>, --token/--license, or FEZD_TOKEN.",
                        FezdExitCodes.UsageError);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.Token);
            }
            req.Headers.TryAddWithoutValidation("X-Fezd-Request-Id", rid);
            customize?.Invoke(req);

            _certFailure = null;
            var sw = Stopwatch.StartNew();
            if (_opts.TraceHttp)
                _opts.Write("trace", $">> {method} {url} rid={rid}" + (auth ? " auth=Bearer ***" : ""));

            HttpResponseMessage resp;
            try
            {
                resp = _http.SendAsync(req).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                throw MapSendException(ex, method, url);
            }

            if (_opts.TraceHttp)
                _opts.Write("trace", $"<< {(int)resp.StatusCode} {method} {url} {sw.ElapsedMilliseconds}ms");
            return resp;
        }

        private RemoteCommsException MapSendException(Exception ex, HttpMethod method, string url)
        {
            if (!string.IsNullOrEmpty(_certFailure))
                return new RemoteCommsException($"TLS: {_certFailure}", ex, FezdExitCodes.ConnectivityError);

            if (ex is System.Threading.Tasks.TaskCanceledException || ex is TimeoutException)
                return new RemoteCommsException($"Request timed out: {method} {url}.", ex, FezdExitCodes.ConnectivityError);

            Exception inner = Innermost(ex);
            if (inner is SocketException se)
                return new RemoteCommsException(
                    $"Cannot reach {_opts.BaseUrl}: {se.SocketErrorCode} ({se.Message}).", ex, FezdExitCodes.ConnectivityError);

            if (inner is System.Security.Authentication.AuthenticationException)
                return new RemoteCommsException(
                    "TLS handshake failed. For a self-signed gateway pass --pin <sha256> or --ca-cert <file>.",
                    ex, FezdExitCodes.ConnectivityError);

            return new RemoteCommsException($"Request failed: {inner.Message}", ex, FezdExitCodes.ConnectivityError);
        }

        private T ReadJson<T>(HttpResponseMessage resp, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> ti)
        {
            string body = ReadBody(resp);
            EnsureSuccess(resp, body);
            try
            {
                return JsonSerializer.Deserialize(body, ti);
            }
            catch (JsonException jx)
            {
                throw new RemoteCommsException($"Malformed response from gateway: {jx.Message}", jx, FezdExitCodes.Error);
            }
        }

        private static string ReadBody(HttpResponseMessage resp) =>
            resp.Content == null ? string.Empty : resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        private void EnsureSuccess(HttpResponseMessage resp, string body)
        {
            if (resp.IsSuccessStatusCode)
                return;

            // Prefer the server's ErrorEnvelope (carries the FEZD exit code).
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    ErrorEnvelope env = JsonSerializer.Deserialize(body, FezdJsonContext.Default.ErrorEnvelope);
                    if (env != null && !string.IsNullOrEmpty(env.Message))
                        throw new RemoteCommsException(
                            env.Message, env.ExitCode != 0 ? env.ExitCode : FezdExitCodes.Error);
                }
                catch (JsonException) { /* fall through to status-based message */ }
            }

            int code = (int)resp.StatusCode;
            string msg = code == 401 || code == 403
                ? $"Unauthorized ({code}): check the token and its scopes."
                : $"Gateway returned HTTP {code} {resp.ReasonPhrase}.";
            throw new RemoteCommsException(msg, code == 401 || code == 403 ? FezdExitCodes.Error : FezdExitCodes.Error);
        }

        // ---- cert pinning ----

        private bool ValidateServerCert(X509Certificate2 cert, X509Chain chain, SslPolicyErrors errors)
        {
            if (cert == null)
            {
                _certFailure = "server presented no certificate";
                return false;
            }

            if (!string.IsNullOrEmpty(_opts.PinSha256))
            {
                string actual = Sha256Hex(cert.RawData);
                string expected = CertPin.Normalize(_opts.PinSha256);
                if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    return true;
                _certFailure = $"certificate pin mismatch (server sha256={actual}, expected={expected})";
                return false;
            }

            if (!string.IsNullOrEmpty(_opts.CaCertPath))
            {
                try
                {
                    using (var ca = new X509Certificate2(_opts.CaCertPath))
                    {
                        if (string.Equals(cert.Thumbprint, ca.Thumbprint, StringComparison.OrdinalIgnoreCase))
                            return true;
                        if (chain != null)
                            foreach (X509ChainElement el in chain.ChainElements)
                                if (string.Equals(el.Certificate.Thumbprint, ca.Thumbprint, StringComparison.OrdinalIgnoreCase))
                                    return true;
                    }
                }
                catch (Exception ex)
                {
                    _certFailure = "failed to load --ca-cert: " + ex.Message;
                    return false;
                }
                _certFailure = "server certificate does not chain to the provided --ca-cert";
                return false;
            }

            if (errors == SslPolicyErrors.None)
                return true;
            _certFailure = $"server certificate is not trusted ({errors}); pass --pin <sha256> or --ca-cert <file> for a self-signed gateway";
            return false;
        }

        // ---- helpers ----

        private static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return ToHex(sha.ComputeHash(fs));
        }

        private static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
                return ToHex(sha.ComputeHash(data));
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static Exception Innermost(Exception ex)
        {
            while (ex.InnerException != null)
                ex = ex.InnerException;
            return ex;
        }

        public void Dispose() => _http?.Dispose();
    }
}
