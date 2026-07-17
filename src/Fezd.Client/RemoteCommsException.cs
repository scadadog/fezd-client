using System;
using Fezd.Contracts;

namespace Fezd.Client
{
    /// <summary>
    /// A remote-communication failure (connect/TLS/pin/auth/HTTP) mapped to an
    /// actionable message + FEZD exit code. The CLI prints the message and returns
    /// the code, so remote failures look like local ones to callers/CI.
    /// </summary>
    public sealed class RemoteCommsException : Exception
    {
        public int ExitCode { get; }

        public RemoteCommsException(string message, int exitCode = FezdExitCodes.ConnectivityError)
            : base(message) => ExitCode = exitCode;

        public RemoteCommsException(string message, Exception inner, int exitCode = FezdExitCodes.ConnectivityError)
            : base(message, inner) => ExitCode = exitCode;
    }
}
