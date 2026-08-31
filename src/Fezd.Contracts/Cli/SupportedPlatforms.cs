namespace Fezd.Contracts.Cli
{
    /// <summary>
    /// Offline copy for <c>fezd-client platforms</c> when no gateway is
    /// configured. The live catalog lives on the connected server's
    /// <c>GET /api/v1/profile</c> response — not in this client.
    /// </summary>
    public static class SupportedPlatforms
    {
        /// <summary>Shown by fezd-client when it cannot reach a gateway profile.</summary>
        public const string ClientOfflineNote =
            "Controller support depends on the connected FEZD gateway. Pass " +
            "--connection <file> (or FEZD_URL) and run platforms again to list " +
            "the families that host can target. For CI, prefer deploy --simulator.";

        /// <summary>Fallback note when a gateway omits profile.note.</summary>
        public const string GatewayNote =
            "The Windows gateway hosts the PLC toolchain. Actual availability depends on the " +
            "installed automation software edition and version, and on the connection driver.";
    }
}
