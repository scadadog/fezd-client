using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>Result of <c>POST /api/v1/projects</c> - the stored project handle
    /// used to start build/deploy/export jobs.</summary>
    public sealed class ProjectUploadResultDto
    {
        [JsonPropertyName("projectId")]
        public string ProjectId { get; set; }

        [JsonPropertyName("fileName")]
        public string FileName { get; set; }

        /// <summary>Lower-case hex SHA-256 the gateway computed and verified.</summary>
        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>True when an identical upload (same sha256) already existed.</summary>
        [JsonPropertyName("deduplicated")]
        public bool Deduplicated { get; set; }
    }
}
