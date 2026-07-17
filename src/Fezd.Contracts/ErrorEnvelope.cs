using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>
    /// Uniform error body returned by the gateway. Carries the FEZD exit code so
    /// the remote client can surface the same diagnostics/exit code as a local run.
    /// </summary>
    public sealed class ErrorEnvelope
    {
        [JsonPropertyName("error")]
        public string Error { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("exitCode")]
        public int ExitCode { get; set; }

        /// <summary>Correlation id echoed from the request (X-Fezd-Request-Id).</summary>
        [JsonPropertyName("requestId")]
        public string RequestId { get; set; }

        public ErrorEnvelope() { }

        public ErrorEnvelope(string error, string message, int exitCode, string requestId = null)
        {
            Error = error;
            Message = message;
            ExitCode = exitCode;
            RequestId = requestId;
        }
    }
}
