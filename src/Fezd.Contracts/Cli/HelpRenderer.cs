using System.Collections.Generic;
using System.Linq;
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
                "FEZD — " + (remoteMode ? CommandCatalog.ClientTagline : CommandCatalog.ServerTagline),
                remoteMode
                    ? "  Remote client (fezd-client). Jobs run on a licensed FEZD gateway."
                    : "  Windows host (fezd-server) with UDE / Control Expert.",
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
                AppendEntry(lines, cmd.Display, ComposeDetailLines(cmd, remoteMode));
            }

            lines.Add("");
            lines.Add("GLOBAL OPTIONS:");
            foreach (GlobalOption opt in CommandCatalog.GlobalOptions)
            {
                if (!opt.IsAvailableIn(remoteMode))
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
            foreach (string line in AttributionFooter(meta, remoteMode, app))
                lines.Add("  " + line);

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Detail lines plus a mode-filtered Options summary derived from
        /// <see cref="CommandInfo.Options"/> (inserted before any Safety: block).
        /// </summary>
        public static string[] ComposeDetailLines(CommandInfo cmd, bool remoteMode)
        {
            var details = new List<string>(cmd.DetailLinesFor(remoteMode));
            CommandOption[] available = cmd.OptionsFor(remoteMode).ToArray();
            if (available.Length == 0)
                return details.ToArray();

            string[] optLines = CommandCatalog.FormatOptionsSummary(available);
            int safetyIdx = details.FindIndex(l =>
                l != null && l.TrimStart().StartsWith("Safety:", System.StringComparison.Ordinal));
            if (safetyIdx >= 0)
                details.InsertRange(safetyIdx, optLines);
            else
                details.AddRange(optLines);
            return details.ToArray();
        }

        /// <summary>The `platforms` reference table.</summary>
        public static string RenderPlatforms(bool remoteMode = false)
        {
            var lines = new List<string>
            {
                "",
                remoteMode
                    ? "  Controller families the FEZD gateway can target"
                    : "  Controller families supported by FEZD",
                "  =====================================",
            };

            foreach (SupportedPlatforms.Platform p in SupportedPlatforms.All)
            {
                lines.Add("   " + p.Family);
                lines.Add("     range : " + p.Prefixes);
                lines.Add("     note  : " + p.Notes);
                lines.Add("");
            }

            string note = remoteMode
                ? SupportedPlatforms.ClientSupportNote
                : SupportedPlatforms.SupportNote;
            foreach (string line in WrapText(note, 74))
                lines.Add("   " + line);
            lines.Add("");

            return string.Join("\n", lines);
        }

        /// <summary>The one-line summary used by --version and the help footer.</summary>
        public static string ShortLine(AppMetadata meta) =>
            $"{meta.Product} v{meta.Version} — {meta.Copyright}";

        /// <summary>Attribution lines appended to help / usage.</summary>
        public static string[] AttributionFooter(AppMetadata meta, bool remoteMode, string appName)
        {
            return new[]
            {
                ShortLine(meta),
                meta.Company + "  |  " + meta.Website + "  |  " + meta.Email,
                "Run '" + appName + " about' for licensing and product information.",
            };
        }

        /// <summary>The full `about` banner.</summary>
        public static string RenderAbout(AppMetadata meta, bool remoteMode = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("  ==================================================");
            sb.AppendLine("   FEZD — FEZ Dispenser");
            if (remoteMode)
            {
                sb.AppendLine("   Remote client (fezd-client)");
                sb.AppendLine("   " + CommandCatalog.ClientTagline);
            }
            else
            {
                sb.AppendLine("   Windows server (fezd-server)");
                sb.AppendLine("   " + CommandCatalog.ServerTagline);
            }
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
                sb.AppendLine("   fezd-client sends .zef projects to a FEZD gateway over HTTPS");
                sb.AppendLine("   for build and simulation (Copia Actions / CI).");
                sb.AppendLine("   A SCADADOG license / connection file (.fezd.env) is required.");
                sb.AppendLine("   Beta access: " + meta.Email);
            }
            else
            {
                sb.AppendLine("   fezd-server hosts the Windows gateway and local automation.");
                sb.AppendLine("   Control Expert must be installed and licensed on the host.");
            }
            sb.AppendLine();
            sb.AppendLine("   Licensing");
            sb.AppendLine("   ---------");
            sb.AppendLine("   FEZD is a product of " + meta.Company + ". Use is subject to your");
            sb.AppendLine("   SCADADOG license / connection agreement. Redistribution of");
            sb.AppendLine("   binaries without authorization is not permitted.");
            sb.AppendLine();
            sb.AppendLine("   Important");
            sb.AppendLine("   ---------");
            sb.AppendLine("   SCADADOG does not take ownership of, or liability for, your");
            sb.AppendLine("   project files. Keep projects under version control (or other");
            sb.AppendLine("   backed-up storage) to avoid loss of changes. This utility is");
            sb.AppendLine("   provided for use at your own risk.");
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
