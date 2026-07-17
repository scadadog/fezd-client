using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>Lifecycle of an exclusive client session (one deploy lease).</summary>
    public enum SessionPhaseDto
    {
        Queued,
        Waiting,
        Running,
        Resetting,
        Succeeded,
        Failed,
        Cancelled
    }

    /// <summary>Body for <c>POST /api/v1/sessions</c> — create a deploy lease.</summary>
    public sealed class CreateSessionRequestDto
    {
        [JsonPropertyName("projectId")]
        public string ProjectId { get; set; }

        /// <summary>When true, connect to the CE simulator (mutually exclusive with a PLC target address).</summary>
        [JsonPropertyName("simulator")]
        public bool Simulator { get; set; }

        [JsonPropertyName("targetAddress")]
        public string TargetAddress { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; } = 502;

        [JsonPropertyName("driver")]
        public string Driver { get; set; } = "TCPIP";

        [JsonPropertyName("run")]
        public bool Run { get; set; }

        [JsonPropertyName("force")]
        public bool Force { get; set; }

        /// <summary>When true, save a .stu under the session artifacts dir and return a download URL.</summary>
        [JsonPropertyName("returnStu")]
        public bool ReturnStu { get; set; }

        [JsonPropertyName("saveSta")]
        public bool SaveSta { get; set; }

        [JsonPropertyName("buildBeforeDeploy")]
        public bool BuildBeforeDeploy { get; set; } = true;

        [JsonPropertyName("appPassword")]
        public string AppPassword { get; set; }

        [JsonPropertyName("appPasswordOld")]
        public string AppPasswordOld { get; set; } = string.Empty;

        [JsonPropertyName("reservationName")]
        public string ReservationName { get; set; } = string.Empty;
    }

    public sealed class ArtifactRefDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("sizeBytes")]
        public long? SizeBytes { get; set; }

        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }
    }

    public sealed class SessionStatusDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("phase")]
        public SessionPhaseDto Phase { get; set; }

        [JsonPropertyName("queuePosition")]
        public int QueuePosition { get; set; }

        [JsonPropertyName("queueDepth")]
        public int QueueDepth { get; set; }

        [JsonPropertyName("eventsUrl")]
        public string EventsUrl { get; set; }

        [JsonPropertyName("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [JsonPropertyName("startedUtc")]
        public DateTime? StartedUtc { get; set; }

        [JsonPropertyName("finishedUtc")]
        public DateTime? FinishedUtc { get; set; }

        [JsonPropertyName("result")]
        public JobResultDto Result { get; set; }

        [JsonPropertyName("artifacts")]
        public List<ArtifactRefDto> Artifacts { get; set; } = new List<ArtifactRefDto>();
    }

    public sealed class SessionEventDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("seq")]
        public long Seq { get; set; }

        [JsonPropertyName("ts")]
        public DateTime Ts { get; set; }

        [JsonPropertyName("level")]
        public string Level { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("phase")]
        public string Phase { get; set; }

        [JsonPropertyName("queuePosition")]
        public int? QueuePosition { get; set; }

        [JsonPropertyName("queueDepth")]
        public int? QueueDepth { get; set; }

        [JsonPropertyName("exitCode")]
        public int? ExitCode { get; set; }

        [JsonPropertyName("artifacts")]
        public List<ArtifactRefDto> Artifacts { get; set; }
    }

    public sealed class SessionTicketDto
    {
        [JsonPropertyName("ticket")]
        public string Ticket { get; set; }

        [JsonPropertyName("expiresUtc")]
        public DateTime ExpiresUtc { get; set; }
    }
}
