using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>Transport shape of an export request (.stu / .sta artifacts).</summary>
    public sealed class ExportRequestDto
    {
        /// <summary>Server-side project id from a prior upload (remote calls). When
        /// set, the gateway resolves it to a path and ignores <see cref="ZefPath"/>.</summary>
        [JsonPropertyName("projectId")]
        public string ProjectId { get; set; }

        [JsonPropertyName("zefPath")]
        public string ZefPath { get; set; }

        [JsonPropertyName("outputDir")]
        public string OutputDir { get; set; }

        [JsonPropertyName("saveStu")]
        public bool SaveStu { get; set; }

        [JsonPropertyName("saveSta")]
        public bool SaveSta { get; set; }

        /// <summary>Rebuild before exporting (defaults to true in the CLI overlay).</summary>
        [JsonPropertyName("build")]
        public bool Build { get; set; } = true;

        [JsonPropertyName("appPassword")]
        public string AppPassword { get; set; }

        [JsonPropertyName("appPasswordOld")]
        public string AppPasswordOld { get; set; } = string.Empty;
    }
}
