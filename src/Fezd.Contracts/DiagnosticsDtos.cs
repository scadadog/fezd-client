using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>Response of <c>GET /api/v1/version</c>.</summary>
    public sealed class VersionDto
    {
        [JsonPropertyName("product")]
        public string Product { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }
    }

    /// <summary>Response of <c>GET /api/v1/whoami</c>.</summary>
    public sealed class WhoAmIDto
    {
        [JsonPropertyName("tokenId")]
        public string TokenId { get; set; }

        [JsonPropertyName("scopes")]
        public List<string> Scopes { get; set; } = new List<string>();
    }
}
