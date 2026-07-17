namespace Fezd.Contracts.Cli
{
    /// <summary>
    /// Controller families FEZD can build/deploy to. Surfaced by
    /// <c>platforms</c> / <c>plcs</c> on both fezd-server and fezd-client.
    /// </summary>
    public static class SupportedPlatforms
    {
        public sealed class Platform
        {
            public string Family { get; set; }
            public string Prefixes { get; set; }
            public string Notes { get; set; }
        }

        public static readonly Platform[] All =
        {
            new Platform
            {
                Family = "Modicon M340",
                Prefixes = "BMX P34 ••••",
                Notes = "Primary tested target (e.g. BMX P34 2020)."
            },
            new Platform
            {
                Family = "Modicon M580 (ePAC)",
                Prefixes = "BME P58 ••••",
                Notes = "Incl. M580 Safety (BME P58 S••••) and Hot Standby (BME H58 ••••)."
            },
            new Platform
            {
                Family = "Modicon MC80",
                Prefixes = "BMK C80 ••••",
                Notes = "Compact controller."
            },
            new Platform
            {
                Family = "Modicon Momentum",
                Prefixes = "171 CBU ••••••",
                Notes = "Ethernet-capable Momentum CPUs."
            },
            new Platform
            {
                Family = "Modicon Quantum",
                Prefixes = "140 CPU ••• ••",
                Notes = "Incl. Quantum Hot Standby and Safety."
            },
            new Platform
            {
                Family = "Modicon Premium / Atrium",
                Prefixes = "TSX P57 •••• / TSX PCI57",
                Notes = "Legacy; Ethernet models deployable over TCP/IP."
            },
            new Platform
            {
                Family = "PLC Simulator",
                Prefixes = "(deploy --simulator)",
                Notes = "Offline test target; no hardware required."
            },
        };

        /// <summary>Support note for fezd-server <c>platforms</c>.</summary>
        public const string SupportNote =
            "The Windows gateway hosts the PLC toolchain. Actual availability depends on the " +
            "installed automation software edition and version, and on the connection driver. " +
            "FEZD's default driver is Modbus/TCP over Ethernet (TCPIP, port 502).";

        /// <summary>Support note for fezd-client <c>platforms</c> (no vendor tooling branding).</summary>
        public const string ClientSupportNote =
            "fezd-client uploads projects to a licensed FEZD gateway over HTTPS. Controller " +
            "support depends on the gateway host. For CI, prefer deploy --simulator (no field hardware).";
    }
}
