using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>
    /// Vendor/toolchain identity advertised by a FEZD gateway
    /// (<c>GET /api/v1/profile</c>). The client stays vendor-neutral and renders
    /// whatever the connected server reports.
    /// </summary>
    public sealed class AutomationProfileDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("vendor")]
        public string Vendor { get; set; }

        [JsonPropertyName("toolchain")]
        public string Toolchain { get; set; }

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; }

        [JsonPropertyName("projectFormats")]
        public List<string> ProjectFormats { get; set; } = new List<string>();

        [JsonPropertyName("platforms")]
        public List<AutomationPlatformDto> Platforms { get; set; } = new List<AutomationPlatformDto>();

        [JsonPropertyName("simulator")]
        public SimulatorProfileDto Simulator { get; set; }

        [JsonPropertyName("note")]
        public string Note { get; set; }
    }

    /// <summary>One controller family the gateway can target.</summary>
    public sealed class AutomationPlatformDto
    {
        [JsonPropertyName("family")]
        public string Family { get; set; }

        [JsonPropertyName("prefixes")]
        public string Prefixes { get; set; }

        [JsonPropertyName("notes")]
        public string Notes { get; set; }

        [JsonPropertyName("simulator")]
        public bool Simulator { get; set; }

        [JsonPropertyName("physical")]
        public bool Physical { get; set; }
    }

    /// <summary>Virtual PLC simulator advertised by the gateway variant.</summary>
    public sealed class SimulatorProfileDto
    {
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; }

        [JsonPropertyName("families")]
        public List<string> Families { get; set; } = new List<string>();
    }

    /// <summary>
    /// Backward-compatible body of <c>GET /api/v1/platforms</c>
    /// (projection of <see cref="AutomationProfileDto"/>).
    /// </summary>
    public sealed class PlatformsResponseDto
    {
        [JsonPropertyName("platforms")]
        public List<AutomationPlatformDto> Platforms { get; set; } = new List<AutomationPlatformDto>();

        [JsonPropertyName("note")]
        public string Note { get; set; }
    }
}
