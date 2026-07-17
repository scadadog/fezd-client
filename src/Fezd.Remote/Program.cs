using System;
using System.Reflection;
using Fezd.Client;
using Fezd.Contracts;
using Fezd.Contracts.Cli;

namespace Fezd.Remote
{
    /// <summary>
    /// The FEZD remote client (<c>fezd-client</c>, Native AOT). Remote-control only:
    /// only Remote/Both catalog verbs are exposed. Connection via
    /// <c>--connection</c> / <c>--remote</c> / env. No UDE or Control Expert.
    /// Help/about are rendered from the shared catalog (filtered to remote mode).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* ignore */ }

            CommandLine cl = CommandLine.Parse(args);
            AppMetadata meta = Metadata();

            if (cl.HasFlag("version"))
            {
                Console.WriteLine("FEZD " + meta.Version + " (fezd-client)");
                Console.WriteLine(meta.Copyright);
                Console.WriteLine(meta.Company + "  |  " + meta.Website);
                return FezdExitCodes.Ok;
            }

            string command = cl.Positionals.Count > 0 ? cl.Positionals[0].ToLowerInvariant() : null;

            if (command == "about")
            {
                Console.WriteLine(HelpRenderer.RenderAbout(meta, remoteMode: true));
                return FezdExitCodes.Ok;
            }
            if (command == "platforms" || command == "plcs")
            {
                Console.WriteLine(HelpRenderer.RenderPlatforms(remoteMode: true));
                return FezdExitCodes.Ok;
            }
            if (IsHelpRequest(command, cl))
            {
                Console.WriteLine(HelpRenderer.RenderUsage(meta, remoteMode: true));
                // Bare invocation (no args) is a usage error; explicit help/? is OK.
                return command == null && !cl.HasFlag("help", "h") && !cl.HasFlag("?")
                    ? FezdExitCodes.UsageError
                    : FezdExitCodes.Ok;
            }

            if (CommandCatalog.IsHostOnlyVerb(command))
            {
                Console.Error.WriteLine(
                    $"ERROR: '{command}' runs on fezd-server (Windows) only and is not available in fezd-client.");
                return FezdExitCodes.UsageError;
            }

            try
            {
                CommandInfo info = CommandCatalog.Find(command);
                string verb = info != null ? info.Name : command;

                switch (verb)
                {
                    case "health":
                        return RemoteCommands.Health(cl);
                    case "build":
                        return RemoteCommands.Build(cl);
                    case "deploy":
                        return RemoteCommands.Deploy(cl);
                    case "export":
                        return RemoteCommands.Export(cl);
                    case "cancel":
                        return RemoteCommands.Cancel(cl);

                    default:
                        Console.Error.WriteLine($"Unknown command: '{command}'.");
                        Console.WriteLine(HelpRenderer.RenderUsage(meta, remoteMode: true));
                        return FezdExitCodes.UsageError;
                }
            }
            catch (RemoteCommsException rce)
            {
                Console.Error.WriteLine("ERROR: " + rce.Message);
                return rce.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL: " + ex.Message);
                return FezdExitCodes.Error;
            }
        }

        private static bool IsHelpRequest(string command, CommandLine cl) =>
            command == null
            || command == "help"
            || command == "?"
            || cl.HasFlag("help", "h")
            || cl.HasFlag("?");

        private static AppMetadata Metadata()
        {
            var meta = new AppMetadata();
            try
            {
                Assembly asm = typeof(Program).Assembly;
                string info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                {
                    int plus = info.IndexOf('+');
                    meta.Version = plus > 0 ? info.Substring(0, plus) : info;
                }
            }
            catch { /* fall back to defaults */ }
            return meta;
        }
    }
}
