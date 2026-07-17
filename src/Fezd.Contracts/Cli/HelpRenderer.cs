using System.Collections.Generic;
using System.Text;

namespace Fezd.Contracts.Cli
{
    /// <summary>
    /// Renders usage, platforms, and the about banner from the shared
    /// <see cref="CommandCatalog"/> and <see cref="AppMetadata"/>. Both the
    /// fezd-server and fezd-client binaries call this, filtered by mode, so their
    /// help can never drift. Kept reflection-free so it is safe under Native AOT.
    /// </summary>
    public static class HelpRenderer
    {
        public const string ServerAppName = "fezd-server";
        public const string ClientAppName = "fezd-client";

        // "  " indent + a 27-wide name column places descriptions at column 29,
        // matching the original hand-formatted help exactly.
        private const int NameColumnWidth = 27;
        private static readonly string ContinuationIndent = new string(' ', 2 + NameColumnWidth);

        /// <summary>Full help / usage text.</summary>
        /// <param name="remoteMode">When true, hide Windows-only verbs (fezd-client).</param>
        public static string RenderUsage(AppMetadata meta, bool remoteMode = false)
        {
            string app = remoteMode ? ClientAppName : ServerAppName;
            var lines = new List<string>
            {
                "FEZD - " + CommandCatalog.Tagline,
                remoteMode
                    ? "  (fezd-client — remote control only; no UDE / Control Expert)"
                    : "  (fezd-server — Windows host with UDE / Control Expert)",
                "",
                "USAGE:",
                "  " + app + " <command> [options]",
                "",
                "COMMANDS:",
            };

            foreach (CommandInfo cmd in CommandCatalog.Commands)
            {
                if (!cmd.IsAvailableIn(remoteMode))
                    continue;
                AppendEntry(lines, cmd.Display, cmd.DetailLines);
            }

            lines.Add("");
            lines.Add("GLOBAL OPTIONS:");
            foreach (GlobalOption opt in CommandCatalog.GlobalOptions)
            {
                bool available = remoteMode
                    ? opt.Availability != CommandAvailability.LocalOnly
                    : opt.Availability != CommandAvailability.Remote;
                if (!available)
                    continue;
                AppendEntry(lines, opt.Display, new[] { opt.Detail });
            }

            lines.Add("");
            lines.Add("EXAMPLES:");
            IEnumerable<string> examples = remoteMode
                ? CommandCatalog.ClientExamples
                : CommandCatalog.ServerExamples;
            foreach (string ex in examples)
                lines.Add("  " + app + " " + ex);

            lines.Add("");
            lines.Add("  " + ShortLine(meta));
            lines.Add("  " + meta.Website + "  |  " + meta.Email);

            return string.Join("\n", lines);
        }

        /// <summary>The `platforms` reference table.</summary>
        public static string RenderPlatforms()
        {
            var lines = new List<string>
            {
                "",
                "  Controller families supported by FEZD",
                "  =====================================",
            };

            foreach (SupportedPlatforms.Platform p in SupportedPlatforms.All)
            {
                lines.Add("   " + p.Family);
                lines.Add("     range : " + p.Prefixes);
                lines.Add("     note  : " + p.Notes);
                lines.Add("");
            }

            foreach (string line in WrapText(SupportedPlatforms.SupportNote, 74))
                lines.Add("   " + line);
            lines.Add("");

            return string.Join("\n", lines);
        }

        /// <summary>The one-line summary used by --version and the help footer.</summary>
        public static string ShortLine(AppMetadata meta) =>
            $"{meta.Product} v{meta.Version} — {meta.Copyright}";

        /// <summary>The full `about` banner.</summary>
        public static string RenderAbout(AppMetadata meta, bool remoteMode = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("  ==================================================");
            sb.AppendLine("   FEZD — FEZ Dispenser");
            if (remoteMode)
                sb.AppendLine("   Remote client (fezd-client)");
            else
                sb.AppendLine("   Windows server (fezd-server)");
            sb.AppendLine("   EcoStruxure Control Expert build/deploy automation");
            sb.AppendLine("  ==================================================");
            sb.AppendLine($"   Version .... {meta.Version}");
            sb.AppendLine($"   Company .... {meta.Company}");
            sb.AppendLine($"   Website .... {meta.Website}");
            sb.AppendLine($"   Email ...... {meta.Email}");
            sb.AppendLine($"   {meta.Copyright}");
            if (!string.IsNullOrWhiteSpace(meta.Description))
            {
                sb.AppendLine();
                sb.AppendLine($"   {meta.Description}");
            }
            sb.AppendLine();
            if (remoteMode)
            {
                sb.AppendLine("   fezd-client sends commands to a FEZD gateway over HTTPS.");
                sb.AppendLine("   A license / connection file is required.");
                sb.AppendLine("   Beta access: " + meta.Email);
            }
            else
            {
                sb.AppendLine("   fezd-server hosts the Windows gateway and local automation.");
                sb.AppendLine("   Control Expert must be installed and licensed on the host.");
            }
            sb.AppendLine();
            sb.AppendLine("   This product drives, but does not include, Schneider Electric");
            sb.AppendLine("   EcoStruxure Control Expert, which must be licensed on the");
            sb.AppendLine("   Windows server host separately.");
            sb.AppendLine();
            sb.AppendLine($"   Support: {meta.Email}   |   {meta.Website}");
            sb.AppendLine();
            return sb.ToString();
        }

        private static void AppendEntry(List<string> lines, string display, string[] detailLines)
        {
            if (detailLines == null || detailLines.Length == 0)
            {
                lines.Add("  " + display);
                return;
            }

            lines.Add("  " + display.PadRight(NameColumnWidth) + detailLines[0]);
            for (int i = 1; i < detailLines.Length; i++)
                lines.Add(ContinuationIndent + detailLines[i]);
        }

        private static IEnumerable<string> WrapText(string text, int width)
        {
            string[] words = (text ?? string.Empty).Split(' ');
            var line = new StringBuilder();
            foreach (string w in words)
            {
                if (line.Length > 0 && line.Length + 1 + w.Length > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(w);
            }
            if (line.Length > 0) yield return line.ToString();
        }
    }
}
