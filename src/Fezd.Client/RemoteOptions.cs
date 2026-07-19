using System;

namespace Fezd.Client
{
    /// <summary>
    /// Connection + trust settings for the remote gateway. Credentials are
    /// <c>FEZD_URL</c> + <c>FEZD_TOKEN</c>. Optional legacy <c>--pin</c> /
    /// <c>--ca-cert</c> are TLS hardening only (not part of the license).
    /// </summary>
    public sealed class RemoteOptions
    {
        public const int ProxiedUploadChunkKb = 1024;
        public const int DirectUploadChunkKb = 16 * 1024;

        /// <summary>Base URL of the gateway, e.g. https://fezd.scadadog.app.</summary>
        public Uri BaseUrl { get; set; }

        /// <summary>Scoped bearer license presented as <c>Authorization: Bearer</c>.</summary>
        public string Token { get; set; }

        /// <summary>
        /// Optional legacy leaf pin (lower-case hex SHA-256). Omit for public CA /
        /// corp TLS-inspection paths; use only for direct self-signed gateways.
        /// </summary>
        public string PinSha256 { get; set; }

        /// <summary>Path to a CA/self-signed cert the server must chain to.</summary>
        public string CaCertPath { get; set; }

        /// <summary>Per-request timeout. Jobs are polled, so this bounds each HTTP call (including each upload part).</summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// Max upload part size in KiB for chunked .zef transfers. Zero selects
        /// 1 MiB through a proxy and 16 MiB for a direct connection.
        /// Server may clamp an explicit value.
        /// </summary>
        public int UploadChunkKb { get; set; }

        /// <summary>When true, always use single-shot <c>POST /api/v1/projects</c>.</summary>
        public bool NoChunkedUpload { get; set; }

        /// <summary>Emit redacted HTTP wire traces (enabled by --debug/--trace).</summary>
        public bool TraceHttp { get; set; }

        /// <summary>
        /// When true, emit debug/trace progress lines (and pass through server
        /// debug <c>log.line</c> events). Enabled by --verbose/--debug/--trace.
        /// </summary>
        public bool Verbose { get; set; }

        /// <summary>How often to poll job status/logs while a job runs.</summary>
        public int PollIntervalMs { get; set; } = 1000;

        /// <summary>
        /// When true, bypass the system HTTP(S) proxy (direct connect). Default
        /// false so corp proxy / PAC settings are honored.
        /// </summary>
        public bool NoProxy { get; set; }

        /// <summary>Sink for progress/log lines: (level, message). Defaults to Console.</summary>
        public Action<string, string> Emit { get; set; }

        internal void Write(string level, string message)
        {
            if (IsQuietLevel(level) && !Verbose && !TraceHttp)
                return;
            Emit?.Invoke(level, message);
        }

        internal int ResolveUploadChunkKb(bool usesProxy)
        {
            return UploadChunkKb > 0
                ? UploadChunkKb
                : usesProxy ? ProxiedUploadChunkKb : DirectUploadChunkKb;
        }

        private static bool IsQuietLevel(string level)
        {
            if (string.IsNullOrEmpty(level)) return false;
            return string.Equals(level, "debug", StringComparison.OrdinalIgnoreCase)
                || string.Equals(level, "trace", StringComparison.OrdinalIgnoreCase);
        }
    }
}
