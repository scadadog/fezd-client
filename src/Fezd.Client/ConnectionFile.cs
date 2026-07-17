using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Fezd.Contracts;

namespace Fezd.Client
{
    /// <summary>
    /// Parses a client connection env file produced by <c>fezd-server license issue</c>
    /// (<c>FEZD_URL</c>, <c>FEZD_TOKEN</c>/<c>FEZD_LICENSE</c>, <c>FEZD_PIN</c>).
    /// </summary>
    public sealed class ConnectionFile
    {
        public string Url { get; set; }
        public string Token { get; set; }
        public string PinSha256 { get; set; }

        public static ConnectionFile Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new RemoteCommsException("Connection file path is empty.", FezdExitCodes.UsageError);
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
                throw new RemoteCommsException(
                    "Connection file not found: '" + path + "'.",
                    FezdExitCodes.UsageError);

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // detectEncodingFromByteOrderMarks handles UTF-8 / UTF-16 Notepad files.
                using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    string raw;
                    while ((raw = reader.ReadLine()) != null)
                    {
                        string trimmed = StripFormatChars(raw).Trim();
                        if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                            continue;
                        if (trimmed.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                            trimmed = trimmed.Substring(7).Trim();
                        int eq = trimmed.IndexOf('=');
                        if (eq <= 0)
                            continue;
                        string key = StripFormatChars(trimmed.Substring(0, eq)).Trim();
                        string value = StripFormatChars(trimmed.Substring(eq + 1)).Trim().Trim('"').Trim('\'');
                        if (key.Length == 0)
                            continue;
                        values[key] = value;
                    }
                }
            }
            catch (RemoteCommsException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new RemoteCommsException(
                    "Failed to read connection file '" + path + "': " + ex.Message,
                    FezdExitCodes.UsageError);
            }

            var result = new ConnectionFile();
            if (values.TryGetValue("FEZD_URL", out string url) && !string.IsNullOrWhiteSpace(url))
                result.Url = url.Trim();
            if (values.TryGetValue("FEZD_TOKEN", out string token) && !string.IsNullOrEmpty(token))
                result.Token = token;
            else if (values.TryGetValue("FEZD_LICENSE", out string license) && !string.IsNullOrEmpty(license))
                result.Token = license;
            if (values.TryGetValue("FEZD_PIN", out string pin) && !string.IsNullOrWhiteSpace(pin))
                result.PinSha256 = CertPin.Normalize(pin);

            return result;
        }

        /// <summary>True when a CLI positional looks like a connection env file path.</summary>
        public static bool LooksLikeConnectionPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            string name = Path.GetFileName(path.Trim().Trim('"').Trim('\''));
            return name.EndsWith(".fezd.env", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "fezd-client.env", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".fezd.env.txt", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Remove BOM / zero-width chars that break KEY matching after copy-paste.</summary>
        private static string StripFormatChars(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\uFEFF' || c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\u2060')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
