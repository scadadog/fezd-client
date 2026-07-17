using System;

namespace Fezd.Contracts
{
    /// <summary>
    /// Normalizes TLS certificate pin strings so the client and server agree on
    /// comparison. Accepts the forms <c>serve</c> prints (<c>sha256:&lt;hex&gt;</c>)
    /// and colon-grouped hex.
    /// </summary>
    public static class CertPin
    {
        public static string Normalize(string s)
        {
            if (s == null)
                return null;
            s = s.Trim();
            if (s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("sha256:".Length);
            else if (s.StartsWith("sha-256:", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("sha-256:".Length);
            return s.Replace(":", "").Replace(" ", "").Trim().ToLowerInvariant();
        }
    }
}
