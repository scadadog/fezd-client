using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>
    /// Source-generated JSON metadata for every contract type. Using a compile-time
    /// context (instead of runtime reflection) is what lets the Linux client be
    /// published with Native AOT / trimming; the net48 gateway reuses it too so
    /// both sides serialize identically.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(DeployRequestDto))]
    [JsonSerializable(typeof(BuildRequestDto))]
    [JsonSerializable(typeof(ExportRequestDto))]
    [JsonSerializable(typeof(DoctorOptionsDto))]
    [JsonSerializable(typeof(DoctorReportDto))]
    [JsonSerializable(typeof(CheckResultDto))]
    [JsonSerializable(typeof(JobResultDto))]
    [JsonSerializable(typeof(JobStatusDto))]
    [JsonSerializable(typeof(ErrorEnvelope))]
    [JsonSerializable(typeof(ProjectUploadResultDto))]
    [JsonSerializable(typeof(JobLogEntryDto))]
    [JsonSerializable(typeof(JobLogsDto))]
    [JsonSerializable(typeof(VersionDto))]
    [JsonSerializable(typeof(WhoAmIDto))]
    [JsonSerializable(typeof(CreateSessionRequestDto))]
    [JsonSerializable(typeof(SessionStatusDto))]
    [JsonSerializable(typeof(SessionEventDto))]
    [JsonSerializable(typeof(SessionTicketDto))]
    [JsonSerializable(typeof(ArtifactRefDto))]
    public partial class FezdJsonContext : JsonSerializerContext
    {
    }
}
