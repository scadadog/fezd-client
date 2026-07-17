namespace Fezd.Contracts
{
    /// <summary>
    /// The single seam every FEZD operation flows through. Two implementations:
    ///   - <c>LocalFezdExecutor</c> (Fezd.Core, Windows) wraps the COM services.
    ///   - <c>RemoteFezdExecutor</c> (Fezd.Client, any OS) makes HTTP calls to the
    ///     gateway.
    /// CLI commands build a DTO and call the selected executor; output and exit
    /// codes are identical either way.
    /// </summary>
    public interface IFezdExecutor
    {
        DoctorReportDto Doctor(DoctorOptionsDto options);
        JobResultDto Build(BuildRequestDto request);
        JobResultDto Deploy(DeployRequestDto request);
        JobResultDto Export(ExportRequestDto request);
    }
}
