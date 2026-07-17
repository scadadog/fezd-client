using System;
using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>Lifecycle phase of an asynchronous gateway job.</summary>
    public enum JobPhaseDto
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Status of an asynchronous job as returned by <c>GET /api/v1/jobs/{id}</c>.
    /// The final result (with exit code) is available once the phase is terminal.
    /// </summary>
    public sealed class JobStatusDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName("phase")]
        public JobPhaseDto Phase { get; set; }

        [JsonPropertyName("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [JsonPropertyName("startedUtc")]
        public DateTime? StartedUtc { get; set; }

        [JsonPropertyName("finishedUtc")]
        public DateTime? FinishedUtc { get; set; }

        [JsonPropertyName("result")]
        public JobResultDto Result { get; set; }
    }
}
