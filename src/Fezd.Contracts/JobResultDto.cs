using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Fezd.Contracts
{
    /// <summary>
    /// Outcome of a build/deploy/export operation. Carries the FEZD
    /// <c>ExitCodes</c> value so a remote caller reproduces the same exit code a
    /// local run would return.
    /// </summary>
    public sealed class JobResultDto
    {
        [JsonPropertyName("exitCode")]
        public int ExitCode { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        /// <summary>Names of produced artifacts (e.g. project.stu, project.sta).</summary>
        [JsonPropertyName("artifacts")]
        public List<string> Artifacts { get; set; } = new List<string>();

        /// <summary>Success result with exit code 0.</summary>
        public static JobResultDto Ok(string message = null) =>
            new JobResultDto { ExitCode = 0, Success = true, Message = message };

        /// <summary>Failure result carrying a FEZD exit code.</summary>
        public static JobResultDto Fail(int exitCode, string message) =>
            new JobResultDto { ExitCode = exitCode, Success = false, Message = message };
    }
}
