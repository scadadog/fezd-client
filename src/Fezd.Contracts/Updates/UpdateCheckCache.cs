using System;
using System.IO;
using System.Text;

namespace Fezd.Contracts.Updates
{
    /// <summary>
    /// Simple file cache so update checks are not chatty (default 24h).
    /// Format: one line <c>unixSeconds|latestVersion|outdated(0|1)</c>.
    /// </summary>
    public static class UpdateCheckCache
    {
        public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

        public static string CachePath(string product) =>
            Path.Combine(Path.GetTempPath(), "fezd-update-check-" + Sanitize(product) + ".txt");

        public static bool TryRead(string product, TimeSpan ttl,
            out string latestVersion, out bool outdated)
        {
            latestVersion = null;
            outdated = false;
            try
            {
                string path = CachePath(product);
                if (!File.Exists(path))
                    return false;
                string line = File.ReadAllText(path, Encoding.UTF8).Trim();
                string[] parts = line.Split('|');
                if (parts.Length < 3)
                    return false;
                if (!long.TryParse(parts[0], out long unix))
                    return false;
                var written = DateTimeOffset.FromUnixTimeSeconds(unix);
                if (DateTimeOffset.UtcNow - written > ttl)
                    return false;
                latestVersion = parts[1];
                outdated = parts[2] == "1";
                return !string.IsNullOrEmpty(latestVersion);
            }
            catch
            {
                return false;
            }
        }

        public static void Write(string product, string latestVersion, bool outdated)
        {
            try
            {
                string line = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + "|" +
                              (latestVersion ?? "") + "|" + (outdated ? "1" : "0");
                File.WriteAllText(CachePath(product), line, Encoding.UTF8);
            }
            catch
            {
                /* best effort */
            }
        }

        private static string Sanitize(string product)
        {
            if (string.IsNullOrWhiteSpace(product))
                return "app";
            var sb = new StringBuilder(product.Length);
            foreach (char c in product)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }
    }
}
