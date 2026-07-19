using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>
    /// Transport shape of a deploy request. Mirrors the fully-resolved
    /// <c>Fezd.Core.Deployment.DeployRequest</c> (config already overlaid with CLI
    /// overrides). The local executor maps this to the core request; the remote
    /// client serializes it to the gateway.
    /// </summary>
    public sealed class DeployRequestDto
    {
        /// <summary>Server-side project id from a prior upload (remote calls). When
        /// set, the gateway resolves it to a path and ignores <see cref="ZefPath"/>.</summary>
        [JsonPropertyName("projectId")]
        public string ProjectId { get; set; }

        /// <summary>Path to the .zef on the machine that runs the operation. For a
        /// remote deploy this is the server-side path resolved from the uploaded
        /// project id.</summary>
        [JsonPropertyName("zefPath")]
        public string ZefPath { get; set; }

        [JsonPropertyName("targetAddress")]
        public string TargetAddress { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; } = 502;

        [JsonPropertyName("driver")]
        public string Driver { get; set; } = "TCPIP";

        [JsonPropertyName("mode")]
        public ConnectionModeDto Mode { get; set; } = ConnectionModeDto.Primary;

        [JsonPropertyName("target")]
        public TargetKindDto Target { get; set; } = TargetKindDto.Plc;

        [JsonPropertyName("buildBeforeDeploy")]
        public bool BuildBeforeDeploy { get; set; } = true;

        [JsonPropertyName("download")]
        public bool Download { get; set; } = true;

        [JsonPropertyName("run")]
        public bool Run { get; set; }

        [JsonPropertyName("force")]
        public bool Force { get; set; }

        /// <summary>
        /// When targeting the local simulator, kill and relaunch sim.exe before
        /// download. Ignored for hardware PLC targets. Default true.
        /// </summary>
        [JsonPropertyName("restartSimulator")]
        public bool RestartSimulator { get; set; } = true;

        [JsonPropertyName("appPassword")]
        public string AppPassword { get; set; }

        [JsonPropertyName("appPasswordOld")]
        public string AppPasswordOld { get; set; } = string.Empty;

        [JsonPropertyName("reservationName")]
        public string ReservationName { get; set; } = string.Empty;

        [JsonPropertyName("outputDir")]
        public string OutputDir { get; set; }

        [JsonPropertyName("saveStu")]
        public bool SaveStu { get; set; }

        [JsonPropertyName("saveSta")]
        public bool SaveSta { get; set; }
    }
}
