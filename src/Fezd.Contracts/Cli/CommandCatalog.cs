using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fezd.Contracts.Cli
{
    /// <summary>
    /// Where a verb can run. <c>fezd-server</c> exposes everything; <c>fezd-client</c>
    /// exposes only <see cref="Remote"/> and <see cref="Both"/> verbs
    /// (Windows-only verbs are hidden and fail with a clear message).
    /// </summary>
    public enum CommandAvailability
    {
        /// <summary>Only meaningful on the Windows host (COM/registration/service).</summary>
        LocalOnly,

        /// <summary>Only meaningful when driving a remote gateway.</summary>
        Remote,

        /// <summary>Available both locally (Windows) and remotely.</summary>
        Both
    }

    /// <summary>A single option surfaced in help and (later) OpenAPI.</summary>
    public sealed class CommandOption
    {
        public string Spec { get; }
        public string Summary { get; }
        public CommandAvailability Availability { get; }

        public CommandOption(string spec, string summary,
            CommandAvailability availability = CommandAvailability.Both)
        {
            Spec = spec;
            Summary = summary;
            Availability = availability;
        }

        public bool IsAvailableIn(bool remoteMode) =>
            remoteMode
                ? Availability != CommandAvailability.LocalOnly
                : Availability != CommandAvailability.Remote;
    }

    /// <summary>
    /// One verb in the shared command catalog. <see cref="Display"/> is the help
    /// column label (e.g. "deploy &lt;zef&gt;"); <see cref="DetailLines"/> are the
    /// exact help lines rendered after the name column. Optional
    /// <see cref="RemoteDetailLines"/> override copy for fezd-client.
    /// Structured <see cref="Options"/> are appended (filtered by mode) so help
    /// cannot drift from the option list.
    /// </summary>
    public sealed class CommandInfo
    {
        public string Name { get; }
        public string Display { get; }
        public string[] Aliases { get; }
        public CommandAvailability Availability { get; }
        public string[] DetailLines { get; }
        public string[] RemoteDetailLines { get; }
        public CommandOption[] Options { get; }

        public CommandInfo(
            string name,
            string display,
            CommandAvailability availability,
            string[] detailLines,
            string[] aliases = null,
            CommandOption[] options = null,
            string[] remoteDetailLines = null)
        {
            Name = name;
            Display = display;
            Availability = availability;
            DetailLines = detailLines ?? new string[0];
            RemoteDetailLines = remoteDetailLines;
            Aliases = aliases ?? new string[0];
            Options = options ?? new CommandOption[0];
        }

        public string[] DetailLinesFor(bool remoteMode) =>
            remoteMode && RemoteDetailLines != null && RemoteDetailLines.Length > 0
                ? RemoteDetailLines
                : DetailLines;

        public bool IsAvailableIn(bool remoteMode) =>
            remoteMode
                ? Availability == CommandAvailability.Remote || Availability == CommandAvailability.Both
                : Availability == CommandAvailability.LocalOnly || Availability == CommandAvailability.Both;

        public IEnumerable<CommandOption> OptionsFor(bool remoteMode) =>
            Options.Where(o => o.IsAvailableIn(remoteMode));
    }

    /// <summary>A global option line shown under GLOBAL OPTIONS.</summary>
    public sealed class GlobalOption
    {
        public string Display { get; }
        public string Detail { get; }
        public CommandAvailability Availability { get; }

        public GlobalOption(string display, string detail,
            CommandAvailability availability = CommandAvailability.Both)
        {
            Display = display;
            Detail = detail;
            Availability = availability;
        }

        public bool IsAvailableIn(bool remoteMode) =>
            remoteMode
                ? Availability != CommandAvailability.LocalOnly
                : Availability != CommandAvailability.Remote;
    }

    /// <summary>
    /// The single source of truth for the command/option surface, shared by
    /// fezd-server and fezd-client so their help/about output can never drift.
    /// </summary>
    public static class CommandCatalog
    {
        /// <summary>fezd-server help / about lead line.</summary>
        public const string ServerTagline = "Windows FEZ Dispenser gateway";

        /// <summary>fezd-client help / about lead line (public marketing).</summary>
        public const string ClientTagline = "PLC Simulator for Copia Actions";

        /// <summary>Legacy alias; prefer <see cref="ServerTagline"/> / <see cref="ClientTagline"/>.</summary>
        public const string Tagline = ServerTagline;

        public static readonly IReadOnlyList<CommandInfo> Commands = new List<CommandInfo>
        {
            new CommandInfo("install", "install", CommandAvailability.LocalOnly, new[]
            {
                "Self-register the UDE COM assets, then run doctor.",
                "Auto-elevates (UAC). Use --no-doctor to skip validation,",
                "--ude-package <dir> to override the asset source.",
            }),
            new CommandInfo("register", "register", CommandAvailability.LocalOnly, new[]
            {
                "Self-register the UDE COM assets only (auto-elevates).",
            }, aliases: new[] { "reg" }),
            new CommandInfo("unregister", "unregister", CommandAvailability.LocalOnly, new[]
            {
                "Remove the UDE COM registration (auto-elevates).",
            }, aliases: new[] { "unreg", "uninstall" }),
            new CommandInfo("doctor", "doctor", CommandAvailability.LocalOnly, new[]
            {
                "Validate this Windows host (OS, Control Expert,",
                "automation broker, license, PLC).",
            }, options: new[]
            {
                new CommandOption("--simulator", "Validate a simulator deployment rather than a physical PLC."),
                new CommandOption("--test-project <path>", "Known-good project for the import/build/save smoke tests."),
                new CommandOption("--deep", "Run the optional connect/download/run check (touches the target)."),
                new CommandOption("--target <addr>", "PLC IP for the reachability probe (--deep). Required unless --simulator."),
                new CommandOption("--port <n>", "PLC TCP port for the reachability probe (default 502)."),
                new CommandOption("--timeout <ms>", "TCP connect timeout for the reachability probe."),
                new CommandOption("--app-password <pwd>", "Application password for deep smoke tests (or set FEZD_APP_PASSWORD)."),
                new CommandOption("--app-password-old <pwd>", "Current password when rotating (rare)."),
            }),
            new CommandInfo("soak", "soak", CommandAvailability.LocalOnly, new[]
            {
                "Monte Carlo robustness campaign: successive UDE jobs with",
                "random inter-launch gaps (default 1–60s) and optional multi-project queue.",
                "Writes trials.csv + trials.jsonl for offline plotting.",
                "Stop the gateway first (fezd-server serve --stop).",
            }, options: new[]
            {
                new CommandOption("--mode new-app|import|deploy-sim|both", "Probe type (default new-app). deploy-sim = full M580→simulator."),
                new CommandOption("--project <path>", "Single local .zef/.stu fixture."),
                new CommandOption("--projects <p1,p2,...>", "Comma-separated fixture queue (different sizes)."),
                new CommandOption("--projects-file <json>", "JSON array of {path,label} fixtures."),
                new CommandOption("--project-sample uniform|round-robin", "How to pick the next project (default uniform)."),
                new CommandOption("--trials <n>", "Number of trials (default 60)."),
                new CommandOption("--duration <sec>", "Wall-clock cap in seconds (default 3600)."),
                new CommandOption("--delay-min <sec>", "Min inter-launch gap (default 1)."),
                new CommandOption("--delay-max <sec>", "Max inter-launch gap (default 60)."),
                new CommandOption("--sample stratified|uniform", "Gap sampling (default stratified)."),
                new CommandOption("--seed <int>", "RNG seed for reproducible campaigns."),
                new CommandOption("--max-failures <n>", "Stop after N consecutive failures (0 = never)."),
                new CommandOption("--keep-sim", "Do not restart sim.exe between deploy-sim trials."),
                new CommandOption("--out-dir <path>", "Directory for trials.csv / trials.jsonl / summary.txt."),
                new CommandOption("--app-password <pwd>", "Application password for import/deploy-sim."),
                new CommandOption("--app-password-old <pwd>", "Current password when rotating (rare)."),
            }),
            new CommandInfo("build", "build <zef>", CommandAvailability.Both, new[]
            {
                "Open and rebuild a project (.zef required today).",
            }, options: new[]
            {
                new CommandOption("--out <dir>", "Directory for the saved .stu."),
                new CommandOption("--stu", "Save a .stu after building."),
                new CommandOption("--app-password <pwd>", "Project application password (or set FEZD_APP_PASSWORD). Applied before build when required."),
                new CommandOption("--app-password-old <pwd>", "Current password when changing to a new one (rare; rotation only)."),
            }, remoteDetailLines: new[]
            {
                "Upload a .zef to the FEZD gateway and rebuild there.",
                "Requires --connection (or FEZD_URL + FEZD_TOKEN).",
            }),
            new CommandInfo("deploy", "deploy <zef>", CommandAvailability.Both, new[]
            {
                "Build, connect, download to PLC, and run (.zef required today).",
                "Safety: aborts if the PLC is in RUN, the target is",
                "already reserved, or a project is already open in the",
                "session. Use --force to stop a running PLC / close an",
                "open project and proceed.",
            }, options: new[]
            {
                new CommandOption("--target <addr>", "PLC address to connect to."),
                new CommandOption("--port <n>", "PLC TCP port (default 502)."),
                new CommandOption("--driver <drv>", "Connection driver (default TCPIP)."),
                new CommandOption("--mode primary|secondary", "Connection mode (fezd-server only; not on remote sessions).", CommandAvailability.LocalOnly),
                new CommandOption("--simulator", "Deploy to the simulator instead of a PLC."),
                new CommandOption("--no-sim-restart", "Do not kill/relaunch local sim.exe before download (simulator deploys only).", CommandAvailability.LocalOnly),
                new CommandOption("--run", "Start the PLC after a successful download."),
                new CommandOption("--build / --no-build", "Build before deploy (default --build)."),
                new CommandOption("--stu", "Save a .stu artifact."),
                new CommandOption("--sta", "Save a .sta artifact."),
                new CommandOption("--out <dir>", "Directory for saved artifacts."),
                new CommandOption("--force", "Stop a running PLC / close an open project / release target connection and proceed."),
                new CommandOption("--app-password <pwd>", "Project application password (or set FEZD_APP_PASSWORD). Applied before build when required."),
                new CommandOption("--app-password-old <pwd>", "Current password when changing to a new one (rare; rotation only)."),
            }, remoteDetailLines: new[]
            {
                "Upload a .zef to the FEZD gateway; build and download to sim or PLC.",
                "Prefer --simulator for CI (no field hardware).",
                "Safety: the gateway aborts if the PLC is in RUN, the target is",
                "already reserved, or a project is already open. Use --force to",
                "stop a running PLC / close an open project and proceed.",
            }),
            new CommandInfo("disconnect", "disconnect", CommandAvailability.LocalOnly, new[]
            {
                "Release a target connection held by the UDE automation session",
                "(best effort). Use before deploy when a prior run left the sim",
                "or PLC reserved. Does not disconnect Control Expert UI — close",
                "CE manually for that.",
            }, options: new[]
            {
                new CommandOption("--simulator", "Disconnect from the simulator instead of a PLC."),
                new CommandOption("--target <addr>", "PLC address (default from config)."),
                new CommandOption("--port <n>", "PLC TCP port (default 502)."),
                new CommandOption("--driver <drv>", "Connection driver (default TCPIP)."),
                new CommandOption("--force", "Aggressively release the target connection."),
            }),
            new CommandInfo("sim", "sim <start|stop|status>", CommandAvailability.Both, new[]
            {
                "Control the server-hosted Control Expert PLC Simulator (sim.exe).",
                "start / stop / status are local on fezd-server; remote fezd-client",
                "supports stop and status (cuts the gateway idle-shutdown wait).",
            }, options: new[]
            {
                new CommandOption("--port <n>", "Simulator TCP port (default from config / 502)."),
            }, remoteDetailLines: new[]
            {
                "sim stop  — stop the gateway host's sim.exe now (cuts the idle wait).",
                "sim status — report whether sim.exe is running and port is listening.",
                "Requires --connection (or FEZD_URL + FEZD_TOKEN) with Control scope for stop.",
            }),
            new CommandInfo("export", "export <zef>", CommandAvailability.Both, new[]
            {
                "Build and export .STU / .STA artifacts (.zef required today).",
            }, options: new[]
            {
                new CommandOption("--stu", "Export a .stu artifact."),
                new CommandOption("--sta", "Export a .sta artifact."),
                new CommandOption("--out <dir>", "Directory for exported artifacts."),
                new CommandOption("--build / --no-build", "Build before export (default --build)."),
                new CommandOption("--app-password <pwd>", "Project application password (or set FEZD_APP_PASSWORD). Applied before build when required."),
                new CommandOption("--app-password-old <pwd>", "Current password when changing to a new one (rare; rotation only)."),
            }, remoteDetailLines: new[]
            {
                "Upload a .zef to the FEZD gateway; build and download .STU / .STA.",
                "Requires --connection (or FEZD_URL + FEZD_TOKEN).",
            }),
            new CommandInfo("inspect", "inspect <project>", CommandAvailability.LocalOnly, new[]
            {
                "Read project CPU identity via UDE COM (no build).",
                "Reports whether an M580 archive needs CE GUI password export.",
            }, options: new[]
            {
                new CommandOption("--json", "Emit machine-readable JSON."),
            }),
            new CommandInfo("serve", "serve", CommandAvailability.LocalOnly, new[]
            {
                "Ensure the gateway is serving via the Windows service (install/start",
                "as needed). If already running, follow gateway logs until Ctrl+C.",
                "Auto-elevates for install/start. UDE needs an interactive desktop session.",
            }, options: new[]
            {
                new CommandOption("--install", "Install the Windows service only (does not start)."),
                new CommandOption("--uninstall", "Stop (if running) and uninstall the Windows service."),
                new CommandOption("--start", "Start the Windows service (error if not installed)."),
                new CommandOption("--stop", "Stop the Windows service."),
                new CommandOption("--restart", "Stop then start the Windows service."),
                new CommandOption("--status", "Print whether the Windows service is installed/running."),
                new CommandOption("--foreground", "Run the HTTPS gateway in this console (debug; no SCM)."),
                new CommandOption("--plain", "With --foreground: scrolling logs instead of the live dashboard (auto when redirected)."),
                new CommandOption("--bind <addr>", "Interface to bind when using --foreground (default 127.0.0.1)."),
                new CommandOption("--port <n>", "TLS port when using --foreground (default 8443)."),
                new CommandOption("--token-store <file>", "Path to the scoped-token store (hashes at rest)."),
                new CommandOption("--cert <pfx>", "Use a provisioned PFX instead of a self-signed cert (--foreground)."),
                new CommandOption("--cert-thumbprint <hash>", "Bind an already-installed certificate (--foreground)."),
                new CommandOption("--print-pin", "Print the legacy cert pin and exit (does not start the server)."),
            }),
            new CommandInfo("pin", "pin", CommandAvailability.LocalOnly, new[]
            {
                "Legacy: print the TLS leaf pin for direct self-signed clients only.",
                "Not part of licensing — prefer public CA + FEZD_TOKEN.",
            }),
            new CommandInfo("cert", "cert refresh", CommandAvailability.LocalOnly, new[]
            {
                "Pick up a win-acme / LocalMachine\\My cert for service.hostname,",
                "write tls.certThumbprint, and rebind HTTP.SYS sslcert. Run elevated",
                "after issuing or renewing Let's Encrypt (hook from win-acme post-renew).",
            }, options: new[]
            {
                new CommandOption("--hostname <host>", "Override service.hostname for the lookup."),
            }),
            new CommandInfo("setup", "setup", CommandAvailability.LocalOnly, new[]
            {
                "Bootstrap the gateway: hostname (required for FEZD_URL), bind/port,",
                "TLS, and open client access by default (license = FEZD_TOKEN).",
            }, options: new[]
            {
                new CommandOption("--hostname <host>", "Required. Reachable DNS/IP written into client FEZD_URL."),
                new CommandOption("--bind <addr>", "Interface to bind (default 0.0.0.0)."),
                new CommandOption("--port <n>", "Port the gateway will serve on (default 8443)."),
                new CommandOption("--allow <ip|cidr>", "Optional lockdown CIDR(s). Omit to allow all (0.0.0.0/0)."),
                new CommandOption("--rotate-cert", "Mint a new self-signed TLS cert (legacy pins become stale)."),
                new CommandOption("--with-pin", "Legacy: also prepare/print a TLS leaf pin for self-signed clients."),
                new CommandOption("--no-pin", "Deprecated no-op (pins are omitted by default)."),
            }),
            new CommandInfo("license", "license <sub>", CommandAvailability.LocalOnly, new[]
            {
                "Manage client licenses (scoped bearer tokens, hashed at rest).",
                "Subcommands: issue | revoke | list.",
                "  issue  — mint FEZD_URL + FEZD_TOKEN connection file (--out); no pin by default.",
                "  revoke — disable by --name or --token-id.",
                "  list   — print id, name, scopes, disabled, expires.",
            }, options: new[]
            {
                new CommandOption("--name <name>", "Friendly license name (issue/revoke)."),
                new CommandOption("--scope <s>", "Token scope: read | write | control (default control)."),
                new CommandOption("--expires-days <n>", "Optional expiry, in days (issue)."),
                new CommandOption("--out <path>", "Connection file to write (issue; default ./<name>.fezd.env)."),
                new CommandOption("--allow <ip|cidr>", "Optional lockdown CIDRs to merge (issue)."),
                new CommandOption("--token-id <id>", "Record id (issue/revoke)."),
                new CommandOption("--with-pin", "Legacy: include FEZD_PIN for direct self-signed only."),
                new CommandOption("--no-pin", "Deprecated no-op (default is no pin)."),
                new CommandOption("--keep-local-copy", "Also write plaintext to server fezd-client.env (issue)."),
                new CommandOption("--force", "Replace the token store instead of appending (issue)."),
            }),
            new CommandInfo("health", "health", CommandAvailability.Remote, new[]
            {
                "Check gateway reachability: TLS trust, bearer auth,",
                "server version, and granted scopes. Aliases: ping, remote.",
                "Prefer --connection <file>; or --remote/--token.",
            }, aliases: new[] { "ping", "remote" }),
            new CommandInfo("cancel", "cancel <session-id>", CommandAvailability.Remote, new[]
            {
                "Cancel a queued or running deploy session on the gateway.",
            }),
            new CommandInfo("update", "update", CommandAvailability.Both, new[]
            {
                "Download and install the latest fezd-server from GitHub Releases.",
                "Subcommands: token --set <pat> | token --clear.",
                "Requires a fine-grained GitHub PAT (Contents: Read on fezd-server).",
            }, options: new[]
            {
                new CommandOption("--thin", "Force the net48 thin drop (default when running Framework).", CommandAvailability.LocalOnly),
                new CommandOption("--self-contained", "Force the net8 self-contained drop.", CommandAvailability.LocalOnly),
            }, remoteDetailLines: new[]
            {
                "Replace this binary with the latest fezd-client release for this OS/arch.",
                "Suggested when a newer version is available.",
            }),
            new CommandInfo("platforms", "platforms", CommandAvailability.Both, new[]
            {
                "List supported Modicon controller families.",
            }, aliases: new[] { "plcs" }, remoteDetailLines: new[]
            {
                "List controller families the FEZD gateway can target.",
            }),
            new CommandInfo("about", "about", CommandAvailability.Both, new[]
            {
                "Show SCADADOG attribution, version, and licensing information.",
                "Also: fezd-server --help | -h | -? | help | ?",
            }, remoteDetailLines: new[]
            {
                "Show SCADADOG attribution, version, and licensing information.",
                "Also: fezd-client --help | -h | -? | help | ?",
            }),
        };

        public static readonly IReadOnlyList<GlobalOption> GlobalOptions = new List<GlobalOption>
        {
            new GlobalOption("--connection <file>", "Load FEZD_URL + FEZD_TOKEN/FEZD_LICENSE from an env file (optional legacy FEZD_PIN).", CommandAvailability.Remote),
            new GlobalOption("--remote <url>", "Target a gateway over HTTPS (or set FEZD_URL).", CommandAvailability.Remote),
            new GlobalOption("--token <value>", "Scoped bearer license (or set FEZD_TOKEN).", CommandAvailability.Remote),
            new GlobalOption("--license <value>", "Alias of --token (or set FEZD_LICENSE).", CommandAvailability.Remote),
            new GlobalOption("--pin <sha256>", "Deprecated. Legacy leaf pin for direct self-signed only; omit for public CA / corp proxy.", CommandAvailability.Remote),
            new GlobalOption("--ca-cert <file>", "Trust a self-signed/CA certificate for the gateway.", CommandAvailability.Remote),
            new GlobalOption("--no-proxy", "Bypass the system HTTP(S) proxy (direct connect).", CommandAvailability.Remote),
            new GlobalOption("--remote-timeout <sec>", "HTTP client timeout per gateway request (default 300; each upload part).", CommandAvailability.Remote),
            new GlobalOption("--upload-chunk-kb <n>", "Max .zef upload part size in KiB (default 1024). Lower for strict corp proxies.", CommandAvailability.Remote),
            new GlobalOption("--no-chunked-upload", "Force single-shot project upload (skip multipart).", CommandAvailability.Remote),
            new GlobalOption("--config <path>", "Path to fezd.config.json (default: next to the executable).", CommandAvailability.LocalOnly),
            new GlobalOption("--log-level <level>", "trace | debug | info | warn | error | fatal.", CommandAvailability.LocalOnly),
            new GlobalOption("--verbose, -v", "Shortcut for --log-level debug (also shows debug session log lines on fezd-client).", CommandAvailability.Both),
            new GlobalOption("--debug", "Debug output (HTTP wire tracing on fezd-client; implies verbose)."),
            new GlobalOption("--trace", "Trace output (HTTP wire tracing on fezd-client; implies verbose)."),
            new GlobalOption("--com-timeout <sec>", "COM session wall-clock limit for build/deploy/export (default 600; 0=off).", CommandAvailability.LocalOnly),
            new GlobalOption("--json | --no-json", "Toggle JSON log file output.", CommandAvailability.LocalOnly),
            new GlobalOption("--version", "Print version."),
            new GlobalOption("--help, -h, -?", "Show this help (also: help | ?)."),
        };

        /// <summary>Example args after the executable name (fezd-server help).</summary>
        public static readonly IReadOnlyList<string> ServerExamples = new List<string>
        {
            "install",
            "register",
            "unregister",
            "doctor",
            "doctor --simulator",
            "soak --mode new-app --trials 10 --seed 1",
            "soak --mode deploy-sim --projects small.zef,large.zef --trials 30 --seed 7",
            @"build project.zef --out C:\build --stu",
            "deploy project.zef --target 192.168.1.10 --driver TCPIP --run",
            "deploy project.zef --target 192.168.1.10 --run --force",
            "deploy project.zef --target 127.0.0.1 --app-password \"<password>\" --run",
            "deploy project.zef --simulator",
            "sim status",
            "sim stop",
            "sim start",
            @"export project.zef --stu --sta --out C:\artifacts",
            "setup --hostname gateway.example --port 8443",
            "license issue --name remote-client --out ./client.fezd.env",
            "license list",
            "pin",
            "serve",
            "serve --status",
            "serve --restart",
            "serve --foreground",
            "serve --foreground --plain",
            "serve --print-pin",
            "update token --set <github-pat>",
            "update",
        };

        /// <summary>Example args after the executable name (fezd-client help).</summary>
        public static readonly IReadOnlyList<string> ClientExamples = new List<string>
        {
            "ping --connection ./client.fezd.env",
            "deploy project.zef --connection ./client.fezd.env --simulator --run",
            "deploy project.zef --connection ./client.fezd.env --target 192.168.1.10 --run",
            "sim stop --connection ./client.fezd.env",
            "sim status --connection ./client.fezd.env",
            "build project.zef --connection ./client.fezd.env --stu --out ./artifacts",
            "export project.zef --connection ./client.fezd.env --stu --out ./artifacts",
            "cancel <session-id> --connection ./client.fezd.env",
            "update",
        };

        /// <summary>Looks up a verb by name or alias (case-insensitive).</summary>
        public static CommandInfo Find(string nameOrAlias)
        {
            if (string.IsNullOrEmpty(nameOrAlias))
                return null;
            return Commands.FirstOrDefault(c =>
                string.Equals(c.Name, nameOrAlias, System.StringComparison.OrdinalIgnoreCase) ||
                c.Aliases.Any(a => string.Equals(a, nameOrAlias, System.StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// True when the verb is Windows-host only (or a retired alias like
        /// <c>provision</c>). Used by fezd-client to reject with a clear message.
        /// </summary>
        public static bool IsHostOnlyVerb(string nameOrAlias)
        {
            if (string.Equals(nameOrAlias, "provision", System.StringComparison.OrdinalIgnoreCase))
                return true;
            // Removed from help; still rejected on fezd-client / accepted as deprecated on server.
            if (string.Equals(nameOrAlias, "service", System.StringComparison.OrdinalIgnoreCase))
                return true;
            CommandInfo cmd = Find(nameOrAlias);
            return cmd != null && cmd.Availability == CommandAvailability.LocalOnly;
        }

        /// <summary>Compact "Options: …" lines for help, wrapping near 60 columns of content.</summary>
        public static string[] FormatOptionsSummary(IEnumerable<CommandOption> options)
        {
            CommandOption[] list = options?.ToArray() ?? new CommandOption[0];
            if (list.Length == 0)
                return new string[0];

            const int wrapAt = 58;
            var lines = new List<string>();
            var current = new StringBuilder("Options: ");
            for (int i = 0; i < list.Length; i++)
            {
                string piece = list[i].Spec;
                string sep = i == list.Length - 1 ? "." : ",";
                string candidate = piece + sep;
                if (current.Length > "Options: ".Length &&
                    current.Length + 1 + candidate.Length > wrapAt)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    current.Append("         ");
                }
                else if (current.Length > "Options: ".Length &&
                         !current.ToString().EndsWith(" "))
                {
                    current.Append(' ');
                }
                current.Append(candidate);
                if (i < list.Length - 1)
                    current.Append(' ');
            }
            if (current.Length > 0)
                lines.Add(current.ToString());
            return lines.ToArray();
        }
    }
}
