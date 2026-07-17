using System;
using System.IO;
using Fezd.Client;
using Fezd.Contracts;
using Fezd.Contracts.Cli;

namespace Fezd.Remote
{
    /// <summary>
    /// Remote-mode verb handlers. Each builds a request DTO from the parsed
    /// command line and runs it through <see cref="RemoteFezdExecutor"/> against the
    /// gateway named by <c>--connection</c> / <c>--remote</c> / <c>FEZD_URL</c>.
    /// </summary>
    internal static class RemoteCommands
    {
        public static int Health(CommandLine cl)
        {
            using (var exec = new RemoteFezdExecutor(BuildOptions(cl)))
            {
                RemoteCheckResult r = exec.Check();
                Console.WriteLine();
                Console.WriteLine($"  Gateway health: {r.Endpoint}");
                Console.WriteLine("  " + new string('-', 50));
                Line("TCP connect", r.TcpOk);
                Line("TLS trust", r.TlsOk);
                Line("Bearer auth (whoami)", r.AuthOk);
                if (!string.IsNullOrEmpty(r.ServerVersion))
                    Console.WriteLine($"         server version : {r.ServerVersion}");
                if (r.Scopes != null && r.Scopes.Count > 0)
                    Console.WriteLine($"         granted scopes : {string.Join(", ", r.Scopes)}");
                Console.WriteLine();

                if (r.Ok)
                {
                    Console.WriteLine("Gateway reachable and authorized.");
                    return FezdExitCodes.Ok;
                }
                Console.Error.WriteLine("ERROR: " + (r.Detail ?? "Gateway health check failed."));
                return FezdExitCodes.ConnectivityError;
            }
        }

        public static int Build(CommandLine cl)
        {
            string zef = RequireZef(cl);
            bool saveStu = cl.HasFlag("stu", "save-stu");
            string outDir = cl.GetOption(new[] { "out", "output" });
            string stuPath = saveStu
                ? Path.Combine(string.IsNullOrEmpty(outDir) ? Directory.GetCurrentDirectory() : outDir,
                    Path.GetFileNameWithoutExtension(zef) + ".stu")
                : null;

            using (var exec = new RemoteFezdExecutor(BuildOptions(cl)))
                return Finish(exec.Build(new BuildRequestDto
                {
                    ZefPath = zef,
                    OutputStuPath = stuPath,
                    SaveStu = saveStu,
                    AppPassword = AppPassword(cl),
                    AppPasswordOld = cl.GetOption("app-password-old", string.Empty)
                }));
        }

        public static int Deploy(CommandLine cl)
        {
            RemoteCliGuards.EnsureDeployFlagsSupported(cl);

            string zef = RequireZef(cl);
            bool simulator = cl.HasFlag("simulator", "sim");
            string targetAddress = cl.GetOption(new[] { "target", "address" });
            if (simulator && string.IsNullOrWhiteSpace(targetAddress))
                targetAddress = "127.0.0.1";
            var req = new DeployRequestDto
            {
                ZefPath = zef,
                TargetAddress = targetAddress,
                Port = cl.GetInt("port", 502),
                Driver = cl.GetOption("driver", "TCPIP"),
                Target = simulator ? TargetKindDto.Simulator : TargetKindDto.Plc,
                BuildBeforeDeploy = cl.GetSwitch("build") ?? true,
                Download = true,
                Run = cl.GetSwitch("run") ?? false,
                Force = cl.HasFlag("force", "f"),
                AppPassword = AppPassword(cl),
                AppPasswordOld = cl.GetOption("app-password-old", string.Empty),
                OutputDir = cl.GetOption(new[] { "out", "output" }),
                SaveStu = cl.GetSwitch("save-stu") ?? cl.HasFlag("stu"),
                SaveSta = cl.GetSwitch("save-sta") ?? cl.HasFlag("sta")
            };

            RemoteOptions opts = BuildOptions(cl);
            Console.WriteLine($"Deploying {zef} to {(simulator ? "SIMULATOR" : req.TargetAddress)} " +
                              $"(driver={req.Driver})...");
            using (var exec = new RemoteFezdExecutor(opts))
                return Finish(exec.Deploy(req));
        }

        public static int Export(CommandLine cl)
        {
            string zef = RequireZef(cl);
            bool saveStu = cl.HasFlag("stu", "save-stu");
            bool saveSta = cl.HasFlag("sta", "save-sta");
            if (!saveStu && !saveSta)
            {
                Console.Error.WriteLine("ERROR: Nothing to export. Specify --stu and/or --sta.");
                return FezdExitCodes.UsageError;
            }
            var req = new ExportRequestDto
            {
                ZefPath = zef,
                OutputDir = cl.GetOption(new[] { "out", "output" }),
                SaveStu = saveStu,
                SaveSta = saveSta,
                Build = cl.GetSwitch("build") ?? true,
                AppPassword = AppPassword(cl),
                AppPasswordOld = cl.GetOption("app-password-old", string.Empty)
            };
            using (var exec = new RemoteFezdExecutor(BuildOptions(cl)))
                return Finish(exec.Export(req));
        }

        public static int Cancel(CommandLine cl)
        {
            if (cl.Positionals.Count < 2 || string.IsNullOrWhiteSpace(cl.Positionals[1]))
                throw new RemoteCommsException(
                    "Missing session id. Usage: fezd-client cancel <session-id> --connection <file>.",
                    FezdExitCodes.UsageError);

            string sessionId = cl.Positionals[1].Trim();
            using (var exec = new RemoteFezdExecutor(BuildOptions(cl)))
            {
                SessionStatusDto status = exec.CancelSession(sessionId);
                Console.WriteLine($"Session {status.Id} cancel requested (phase={status.Phase}).");
                return FezdExitCodes.Ok;
            }
        }

        // ---- helpers ----

        private static int Finish(JobResultDto result)
        {
            if (!result.Success && !string.IsNullOrEmpty(result.Message))
            {
                Console.Error.WriteLine("ERROR: " + result.Message);
                if (result.ExitCode == FezdExitCodes.BuildError &&
                    result.Message.IndexOf("app-password", StringComparison.OrdinalIgnoreCase) < 0 &&
                    result.Message.IndexOf("Protected Engineering Link", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Console.Error.WriteLine(
                        "HINT: If this project needs an application password, re-run with " +
                        "--app-password <pwd> (or set FEZD_APP_PASSWORD).");
                }
            }
            return result.ExitCode;
        }

        /// <summary>
        /// Resolve remote options: connection file / FEZD_CONNECTION first, then env,
        /// then CLI flags. Non-loopback hosts require HTTPS. Pin / --ca-cert are
        /// optional: omit them to trust the system CA store (public Let's Encrypt,
        /// corp TLS-inspection proxies). Keep FEZD_PIN only when pinning a known leaf
        /// (e.g. self-signed direct path); pinning breaks under TLS inspection.
        /// </summary>
        internal static RemoteOptions BuildOptions(CommandLine cl) => RequireRemote(cl);

        internal static RemoteOptions RequireRemote(CommandLine cl)
        {
            string url = null;
            string token = null;
            string pin = null;
            string connectionPathUsed = null;

            if (cl.HasFlag("connection") && string.IsNullOrEmpty(cl.GetOption("connection")))
            {
                throw new RemoteCommsException(
                    "Missing path for --connection. Example: fezd-client ping --connection ./client.fezd.env",
                    FezdExitCodes.UsageError);
            }

            string connectionPath = cl.GetOption("connection")
                ?? Environment.GetEnvironmentVariable("FEZD_CONNECTION")
                ?? InferConnectionPath(cl);
            if (!string.IsNullOrWhiteSpace(connectionPath))
            {
                connectionPathUsed = connectionPath;
                ConnectionFile conn = ConnectionFile.Parse(connectionPath);
                if (!string.IsNullOrEmpty(conn.Url))
                    url = conn.Url;
                if (!string.IsNullOrEmpty(conn.Token))
                    token = conn.Token;
                if (!string.IsNullOrEmpty(conn.PinSha256))
                    pin = conn.PinSha256;
            }

            string envUrl = Environment.GetEnvironmentVariable("FEZD_URL");
            if (!string.IsNullOrWhiteSpace(envUrl))
                url = envUrl;
            string envToken = Environment.GetEnvironmentVariable("FEZD_TOKEN")
                ?? Environment.GetEnvironmentVariable("FEZD_LICENSE");
            if (!string.IsNullOrWhiteSpace(envToken))
                token = envToken;
            string envPin = Environment.GetEnvironmentVariable("FEZD_PIN");
            if (!string.IsNullOrWhiteSpace(envPin))
                pin = envPin;

            string remoteFlag = cl.GetOption("remote");
            if (!string.IsNullOrEmpty(remoteFlag))
                url = remoteFlag;
            string tokenFlag = cl.GetOption("token") ?? cl.GetOption("license");
            if (!string.IsNullOrEmpty(tokenFlag))
                token = tokenFlag;
            string pinFlag = cl.GetOption("pin");
            if (!string.IsNullOrEmpty(pinFlag))
                pin = pinFlag;
            string caCert = cl.GetOption(new[] { "ca-cert", "cacert" });

            if (string.IsNullOrEmpty(url))
            {
                if (!string.IsNullOrEmpty(connectionPathUsed))
                {
                    throw new RemoteCommsException(
                        "Connection file '" + connectionPathUsed + "' has no FEZD_URL=... line. " +
                        "Expected FEZD_URL=https://fezd.scadadog.com (and FEZD_TOKEN=...).",
                        FezdExitCodes.UsageError);
                }
                throw new RemoteCommsException(
                    "Missing gateway URL. Pass --connection <file>, --remote <https url>, or set FEZD_URL. " +
                    "Example: fezd-client ping --connection ./client.fezd.env",
                    FezdExitCodes.UsageError);
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri baseUrl) ||
                (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp))
                throw new RemoteCommsException($"Invalid gateway URL: '{url}'.", FezdExitCodes.UsageError);

            RemoteCliGuards.EnsureSecureTransport(baseUrl);

            if (!string.IsNullOrEmpty(pin))
            {
                Emit("warn",
                    "FEZD_PIN/--pin is deprecated legacy TLS pinning. Prefer system CA trust " +
                    "(public Let's Encrypt or corp proxy root) with FEZD_TOKEN only. " +
                    "Pinning breaks under TLS inspection.");
            }

            return new RemoteOptions
            {
                BaseUrl = baseUrl,
                Token = token,
                PinSha256 = pin,
                CaCertPath = caCert,
                TimeoutSeconds = cl.GetInt("remote-timeout", 300),
                TraceHttp = cl.HasFlag("debug") || cl.HasFlag("trace"),
                NoProxy = cl.HasFlag("no-proxy"),
                Emit = (level, msg) => Emit(level, msg)
            };
        }

        /// <summary>
        /// Accept <c>fezd-client ping ./client.fezd.env</c> (path as positional) in addition
        /// to <c>--connection</c>, which is a common mistake from the license-file comment.
        /// </summary>
        private static string InferConnectionPath(CommandLine cl)
        {
            for (int i = 1; i < cl.Positionals.Count; i++)
            {
                string candidate = cl.Positionals[i];
                if (ConnectionFile.LooksLikeConnectionPath(candidate))
                    return candidate.Trim().Trim('"').Trim('\'');
            }
            return null;
        }

        private static void Emit(string level, string message)
        {
            string tag = (level ?? "info").ToUpperInvariant();
            if (string.Equals(level, "error", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase))
                Console.Error.WriteLine($"[{tag}] {message}");
            else
                Console.WriteLine($"[{tag}] {message}");
        }

        private static string AppPassword(CommandLine cl) =>
            cl.GetOption("app-password") ?? Environment.GetEnvironmentVariable("FEZD_APP_PASSWORD");

        private static string RequireZef(CommandLine cl)
        {
            if (cl.Positionals.Count < 2)
                throw new RemoteCommsException(
                    "Missing project file. Usage: fezd-client <command> <zef-file> --connection <file> [options].",
                    FezdExitCodes.UsageError);
            return cl.Positionals[1];
        }

        private static void Line(string label, bool ok) =>
            Console.WriteLine($"  [ {(ok ? "OK " : "FAIL")} ] {label}");
    }
}
