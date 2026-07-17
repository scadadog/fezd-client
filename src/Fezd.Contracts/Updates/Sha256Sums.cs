using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Fezd.Contracts.Updates
{
    /// <summary>Parse and verify GitHub release <c>SHA256SUMS.txt</c> files.</summary>
    public static class Sha256Sums
    {
        public static bool TryGetExpectedHash(string sumsFileContent, string assetFileName, out string expectedHex)
        {
            expectedHex = null;
            if (string.IsNullOrEmpty(sumsFileContent) || string.IsNullOrEmpty(assetFileName))
                return false;

            string target = Path.GetFileName(assetFileName);
            using (var reader = new StringReader(sumsFileContent))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    // "hash  filename" or "hash *filename"
                    int sp = line.IndexOfAny(new[] { ' ', '\t' });
                    if (sp <= 0)
                        continue;
                    string hash = line.Substring(0, sp).Trim();
                    string name = line.Substring(sp).Trim();
                    if (name.StartsWith("*", StringComparison.Ordinal))
                        name = name.Substring(1).Trim();
                    name = Path.GetFileName(name.Trim('"'));
                    if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase) &&
                        hash.Length == 64)
                    {
                        expectedHex = hash.ToLowerInvariant();
                        return true;
                    }
                }
            }
            return false;
        }

        public static string ComputeFileHashHex(string filePath)
        {
            using (var sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static bool VerifyFile(string filePath, string sumsFileContent, string assetFileName)
        {
            if (!TryGetExpectedHash(sumsFileContent, assetFileName, out string expected))
                return false;
            string actual = ComputeFileHashHex(filePath);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
