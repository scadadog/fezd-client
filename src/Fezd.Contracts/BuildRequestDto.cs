using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>Transport shape of a build request.</summary>
    public sealed class BuildRequestDto
    {
        /// <summary>Server-side project id from a prior upload (remote calls). When
        /// set, the gateway resolves it to a path and ignores <see cref="ZefPath"/>.</summary>
        [JsonPropertyName("projectId")]
        public string ProjectId { get; set; }

        [JsonPropertyName("zefPath")]
        public string ZefPath { get; set; }

        /// <summary>Where to write the rebuilt .stu, or null/empty to skip saving.
        /// For remote jobs the gateway chooses the path; set <see cref="SaveStu"/>.</summary>
        [JsonPropertyName("outputStuPath")]
        public string OutputStuPath { get; set; }

        /// <summary>Remote request: save a .stu (gateway picks the artifact path).</summary>
        [JsonPropertyName("saveStu")]
        public bool SaveStu { get; set; }

        [JsonPropertyName("appPassword")]
        public string AppPassword { get; set; }

        [JsonPropertyName("appPasswordOld")]
        public string AppPasswordOld { get; set; } = string.Empty;
    }
}
