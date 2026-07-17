using System;

namespace Fezd.Contracts.Updates
{
    /// <summary>Minimal SemVer compare for update checks (major.minor.patch).</summary>
    public static class SemVer
    {
        /// <summary>
        /// Returns &gt;0 if <paramref name="a"/> is newer than <paramref name="b"/>,
        /// &lt;0 if older, 0 if equal. Non-numeric / empty versions compare as 0.0.0.
        /// Pre-release suffixes after '-' are ignored for ordering.
        /// </summary>
        public static int Compare(string a, string b)
        {
            Parse(a, out int aMaj, out int aMin, out int aPat);
            Parse(b, out int bMaj, out int bMin, out int bPat);
            if (aMaj != bMaj) return aMaj.CompareTo(bMaj);
            if (aMin != bMin) return aMin.CompareTo(bMin);
            return aPat.CompareTo(bPat);
        }

        public static bool IsNewer(string candidate, string current) =>
            Compare(candidate, current) > 0;

        public static string Normalize(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return "0.0.0";
            string v = version.Trim();
            if (v.Length > 0 && (v[0] == 'v' || v[0] == 'V'))
                v = v.Substring(1);
            int plus = v.IndexOf('+');
            if (plus >= 0)
                v = v.Substring(0, plus);
            int dash = v.IndexOf('-');
            if (dash >= 0)
                v = v.Substring(0, dash);
            return v;
        }

        private static void Parse(string version, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            string v = Normalize(version);
            string[] parts = v.Split('.');
            if (parts.Length > 0) int.TryParse(parts[0], out major);
            if (parts.Length > 1) int.TryParse(parts[1], out minor);
            if (parts.Length > 2) int.TryParse(parts[2], out patch);
        }
    }
}
