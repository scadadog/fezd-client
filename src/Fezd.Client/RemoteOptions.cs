using System;

namespace Fezd.Client
{
    /// <summary>
    /// Connection + trust settings for the remote gateway. Populated from
    /// <c>--remote</c>, <c>--token</c>, and the pin flags (<c>--pin</c> /
    /// <c>--ca-cert</c>).
    /// </summary>
    public sealed class RemoteOptions
    {
        /// <summary>Base URL of the gateway, e.g. https://host:8443.</summary>
        public Uri BaseUrl { get; set; }

        /// <summary>Scoped bearer token presented as <c>Authorization: Bearer</c>.</summary>
        public string Token { get; set; }

        /// <summary>Lower-case hex SHA-256 of the pinned leaf certificate (cert pin).</summary>
        public string PinSha256 { get; set; }

        /// <summary>Path to a CA/self-signed cert the server must chain to.</summary>
        public string CaCertPath { get; set; }

        /// <summary>Per-request timeout. Jobs are polled, so this bounds each HTTP call.</summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>Emit redacted HTTP wire traces (enabled by --debug/--trace).</summary>
        public bool TraceHttp { get; set; }

        /// <summary>How often to poll job status/logs while a job runs.</summary>
        public int PollIntervalMs { get; set; } = 1000;

        /// <summary>Sink for progress/log lines: (level, message). Defaults to Console.</summary>
        public Action<string, string> Emit { get; set; }

        internal void Write(string level, string message) => Emit?.Invoke(level, message);
    }
}
