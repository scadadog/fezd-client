namespace Fezd.Contracts.Cli
{
    /// <summary>
    /// Controller families FEZD can build/deploy to. FEZD drives EcoStruxure
    /// Control Expert through the UDE broker, so the effective support set is
    /// "whatever the installed Control Expert edition/version supports". This is
    /// the reference list surfaced by `fezd platforms` and the docs. Lives in the
    /// shared contracts so the Windows and Linux binaries render it identically.
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
                Prefixes = "(fezd deploy --simulator)",
                Notes = "Offline test target; no hardware required."
            },
        };

        /// <summary>Human-readable support note shown alongside the list.</summary>
        public const string SupportNote =
            "FEZD deploys through EcoStruxure Control Expert via the UDE broker. Actual availability " +
            "depends on the installed Control Expert edition (S/L/XL/XLS) and version, and on the " +
            "connection driver. FEZD's default driver is Modbus/TCP over Ethernet (TCPIP, port 502).";
    }
}
