using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Fezd.Contracts.Updates
{
    /// <summary>
    /// Best-effort GitHub Releases client. Optional Bearer token for private repos.
    /// Uses <see cref="JsonDocument"/> (AOT/trim safe) rather than reflection.
    /// </summary>
    public sealed class GitHubReleaseClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _owner;
        private readonly string _repo;
        private readonly string _token;

        public GitHubReleaseClient(string owner, string repo, string token = null,
            HttpClient http = null, TimeSpan? timeout = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
            if (http != null)
            {
                _http = http;
                _ownsHttp = false;
            }
            else
            {
                _http = new HttpClient();
                _ownsHttp = true;
                _http.Timeout = timeout ?? TimeSpan.FromSeconds(30);
            }
        }

        /// <summary>
        /// Fetch the latest release tag/version. Returns false on any failure.
        /// </summary>
        public bool TryGetLatestVersion(out string version, out string tagName,
            TimeSpan? timeout = null)
        {
            version = null;
            tagName = null;
            try
            {
                using (var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3)))
                {
                    GitHubRelease release = GetLatestReleaseAsync(cts.Token)
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    if (release == null || string.IsNullOrEmpty(release.TagName))
                        return false;
                    tagName = release.TagName;
                    version = SemVer.Normalize(release.TagName);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public GitHubRelease GetLatestRelease(CancellationToken cancellationToken = default) =>
            GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

        public async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
        {
            string url = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";
            using (HttpRequestMessage req = CreateRequest(HttpMethod.Get, url, apiJson: true))
            using (HttpResponseMessage resp = await _http.SendAsync(req, cancellationToken)
                       .ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ParseRelease(json);
            }
        }

        /// <summary>
        /// Download a named release asset to <paramref name="destPath"/>.
        /// Private repos must use the API asset URL with a token.
        /// </summary>
        public void DownloadAsset(GitHubRelease release, string assetName, string destPath,
            CancellationToken cancellationToken = default) =>
            DownloadAssetAsync(release, assetName, destPath, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();

        public async Task DownloadAssetAsync(GitHubRelease release, string assetName, string destPath,
            CancellationToken cancellationToken = default)
        {
            if (release == null)
                throw new ArgumentNullException(nameof(release));
            GitHubAsset asset = release.FindAsset(assetName);
            if (asset == null)
                throw new InvalidOperationException(
                    $"Release asset '{assetName}' not found in {_owner}/{_repo} {release.TagName}.");

            string downloadUrl;
            bool apiAsset;
            if (!string.IsNullOrEmpty(_token) && !string.IsNullOrEmpty(asset.ApiUrl))
            {
                downloadUrl = asset.ApiUrl;
                apiAsset = true;
            }
            else if (!string.IsNullOrEmpty(asset.BrowserDownloadUrl))
            {
                downloadUrl = asset.BrowserDownloadUrl;
                apiAsset = false;
            }
            else
            {
                throw new InvalidOperationException(
                    $"No download URL for asset '{assetName}'.");
            }

            string dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using (HttpRequestMessage req = CreateRequest(HttpMethod.Get, downloadUrl, apiJson: false))
            {
                if (apiAsset)
                {
                    req.Headers.Accept.Clear();
                    req.Headers.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                }

                using (HttpResponseMessage resp = await _http.SendAsync(
                           req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                           .ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    using (Stream src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream dst = File.Create(destPath))
                        await CopyToAsync(src, dst, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public string DownloadAssetText(GitHubRelease release, string assetName,
            CancellationToken cancellationToken = default)
        {
            string temp = Path.Combine(Path.GetTempPath(),
                "fezd-asset-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                DownloadAsset(release, assetName, temp, cancellationToken);
                return File.ReadAllText(temp);
            }
            finally
            {
                try { File.Delete(temp); } catch { /* ignore */ }
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url, bool apiJson)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.UserAgent.ParseAdd("fezd-update");
            if (apiJson)
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            if (!string.IsNullOrEmpty(_token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return req;
        }

        private static GitHubRelease ParseRelease(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                var release = new GitHubRelease
                {
                    TagName = root.TryGetProperty("tag_name", out JsonElement tag)
                        ? tag.GetString()
                        : null
                };
                if (root.TryGetProperty("assets", out JsonElement assets) &&
                    assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement a in assets.EnumerateArray())
                    {
                        release.Assets.Add(new GitHubAsset
                        {
                            Name = a.TryGetProperty("name", out JsonElement n) ? n.GetString() : null,
                            ApiUrl = a.TryGetProperty("url", out JsonElement u) ? u.GetString() : null,
                            BrowserDownloadUrl = a.TryGetProperty("browser_download_url", out JsonElement b)
                                ? b.GetString()
                                : null
                        });
                    }
                }
                return release;
            }
        }

        private static async Task CopyToAsync(Stream src, Stream dst, CancellationToken ct)
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                await dst.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_ownsHttp)
                _http.Dispose();
        }
    }

    public sealed class GitHubRelease
    {
        public string TagName { get; set; }
        public System.Collections.Generic.List<GitHubAsset> Assets { get; } =
            new System.Collections.Generic.List<GitHubAsset>();

        public GitHubAsset FindAsset(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            foreach (GitHubAsset a in Assets)
            {
                if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                    return a;
            }
            return null;
        }
    }

    public sealed class GitHubAsset
    {
        public string Name { get; set; }
        public string ApiUrl { get; set; }
        public string BrowserDownloadUrl { get; set; }
    }
}
