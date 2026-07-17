using System.Collections.Generic;

namespace Fezd.Client
{
    /// <summary>
    /// Outcome of <c>fezd remote check</c> / <c>fezd ping</c>: each rung of the
    /// connect ladder plus what the server reported.
    /// </summary>
    public sealed class RemoteCheckResult
    {
        public string Endpoint { get; set; }
        public bool TcpOk { get; set; }
        public bool TlsOk { get; set; }
        public bool PinOk { get; set; }
        public bool AuthOk { get; set; }
        public string ServerVersion { get; set; }
        public List<string> Scopes { get; set; } = new List<string>();

        /// <summary>First failure detail, if any (used for the exit message).</summary>
        public string Detail { get; set; }

        /// <summary>True when every attempted rung passed.</summary>
        public bool Ok => TcpOk && TlsOk && PinOk && AuthOk;
    }
}
