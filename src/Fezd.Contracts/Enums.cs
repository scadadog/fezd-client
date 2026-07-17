namespace Fezd.Contracts
{
    /// <summary>
    /// Wire-level mirror of <c>Fezd.Core.Com.ConnectionMode</c>. Duplicated here so
    /// the netstandard2.0 contracts stay free of any COM/net48 dependency; the
    /// local executor maps between the two.
    /// </summary>
    public enum ConnectionModeDto
    {
        Unknown = 0,
        Primary = 1,
        Secondary = 2
    }

    /// <summary>
    /// Wire-level mirror of <c>Fezd.Core.Com.TargetConnection</c> (PLC vs the
    /// offline simulator).
    /// </summary>
    public enum TargetKindDto
    {
        Plc = 0,
        Simulator = 1
    }

    /// <summary>Wire-level mirror of <c>Fezd.Core.Diagnostics.CheckStatus</c>.</summary>
    public enum CheckStatusDto
    {
        Pass,
        Warn,
        Fail,
        Skip
    }
}
