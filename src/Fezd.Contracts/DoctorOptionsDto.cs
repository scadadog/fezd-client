using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>Transport shape of <c>Fezd.Core.Diagnostics.DoctorOptions</c>.</summary>
    public sealed class DoctorOptionsDto
    {
        [JsonPropertyName("simulator")]
        public bool Simulator { get; set; }

        [JsonPropertyName("targetAddress")]
        public string TargetAddress { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; } = 502;

        [JsonPropertyName("connectTimeoutMs")]
        public int ConnectTimeoutMs { get; set; } = 3000;

        [JsonPropertyName("testProjectPath")]
        public string TestProjectPath { get; set; }

        [JsonPropertyName("deep")]
        public bool Deep { get; set; }

        /// <summary>Root of the FEZD install (server-side; not sent by remote clients).</summary>
        [JsonPropertyName("installRoot")]
        public string InstallRoot { get; set; }

        [JsonPropertyName("appPassword")]
        public string AppPassword { get; set; }

        [JsonPropertyName("appPasswordOld")]
        public string AppPasswordOld { get; set; } = string.Empty;
    }
}
