using System;
using System.Collections.Generic;
using System.IO;
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
                foreach (string line in File.ReadAllLines(path))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                        continue;
                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    values[trimmed.Substring(0, eq).Trim()] = trimmed.Substring(eq + 1).Trim().Trim('"').Trim('\'');
                }
            }
            catch (Exception ex)
            {
                throw new RemoteCommsException(
                    "Failed to read connection file '" + path + "': " + ex.Message,
                    FezdExitCodes.UsageError);
            }

            var result = new ConnectionFile();
            if (values.TryGetValue("FEZD_URL", out string url))
                result.Url = url;
            if (values.TryGetValue("FEZD_TOKEN", out string token) && !string.IsNullOrEmpty(token))
                result.Token = token;
            else if (values.TryGetValue("FEZD_LICENSE", out string license) && !string.IsNullOrEmpty(license))
                result.Token = license;
            if (values.TryGetValue("FEZD_PIN", out string pin))
                result.PinSha256 = CertPin.Normalize(pin);

            return result;
        }
    }
}
