using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>Transport shape of a single doctor check result.</summary>
    public sealed class CheckResultDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("status")]
        public CheckStatusDto Status { get; set; }

        [JsonPropertyName("detail")]
        public string Detail { get; set; }

        [JsonPropertyName("remedy")]
        public string Remedy { get; set; }
    }

    /// <summary>Transport shape of <c>Fezd.Core.Diagnostics.DoctorReport</c>.</summary>
    public sealed class DoctorReportDto
    {
        [JsonPropertyName("results")]
        public List<CheckResultDto> Results { get; set; } = new List<CheckResultDto>();

        [JsonIgnore]
        public int PassCount => Results.Count(r => r.Status == CheckStatusDto.Pass);

        [JsonIgnore]
        public int WarnCount => Results.Count(r => r.Status == CheckStatusDto.Warn);

        [JsonIgnore]
        public int FailCount => Results.Count(r => r.Status == CheckStatusDto.Fail);

        [JsonIgnore]
        public int SkipCount => Results.Count(r => r.Status == CheckStatusDto.Skip);

        /// <summary>True when no check failed (warnings are tolerated).</summary>
        [JsonIgnore]
        public bool Healthy => FailCount == 0;
    }
}
