using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>A single captured log line from a job's execution.</summary>
    public sealed class JobLogEntryDto
    {
        /// <summary>Monotonic sequence number (cursor for incremental polling).</summary>
        [JsonPropertyName("seq")]
        public long Seq { get; set; }

        [JsonPropertyName("ts")]
        public DateTime Ts { get; set; }

        [JsonPropertyName("level")]
        public string Level { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }

    /// <summary>
    /// Incremental log response for <c>GET /api/v1/jobs/{id}/logs?after=N</c>.
    /// Clients poll with <see cref="NextCursor"/> until <see cref="Done"/> is true.
    /// </summary>
    public sealed class JobLogsDto
    {
        [JsonPropertyName("entries")]
        public List<JobLogEntryDto> Entries { get; set; } = new List<JobLogEntryDto>();

        [JsonPropertyName("nextCursor")]
        public long NextCursor { get; set; }

        /// <summary>True once the job is terminal and no more logs will arrive.</summary>
        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }
}
