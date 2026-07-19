using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>Status of the gateway-hosted Control Expert PLC Simulator (sim.exe).</summary>
    public sealed class SimulatorStatusDto
    {
        [JsonPropertyName("managed")]
        public bool Managed { get; set; }

        [JsonPropertyName("running")]
        public bool Running { get; set; }

        [JsonPropertyName("portListening")]
        public bool PortListening { get; set; }

        [JsonPropertyName("pid")]
        public int? Pid { get; set; }

        [JsonPropertyName("exePath")]
        public string ExePath { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("startedByFezd")]
        public bool StartedByFezd { get; set; }

        [JsonPropertyName("resolveError")]
        public string ResolveError { get; set; }
    }

    /// <summary>Result of POST /api/v1/simulator/stop.</summary>
    public sealed class SimulatorStopResultDto
    {
        /// <summary>True when a running sim.exe was stopped; false if none was running.</summary>
        [JsonPropertyName("stopped")]
        public bool Stopped { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
