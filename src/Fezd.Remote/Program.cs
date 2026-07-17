using System;
using System.IO;
using System.Reflection;
using Fezd.Client;
using Fezd.Contracts;
using Fezd.Contracts.Cli;

namespace Fezd.Remote
{
    /// <summary>
    /// The FEZD remote client (<c>fezd-client</c>, Native AOT). Remote-control only:
    /// only the Remote/Both verbs are exposed, each requires <c>--remote</c>, and
    /// the job's exit code is reproduced locally. No UDE or Control Expert. Help/about
    /// are rendered from the shared catalog (filtered to remote mode).
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
                Console.WriteLine(HelpRenderer.RenderPlatforms());
                return FezdExitCodes.Ok;
            }
            if (command == null || command == "help" || cl.HasFlag("help", "h"))
            {
                Console.WriteLine(HelpRenderer.RenderUsage(meta, remoteMode: true));
                return command == null && !cl.HasFlag("help", "h") ? FezdExitCodes.UsageError : FezdExitCodes.Ok;
            }

            try
            {
                switch (command)
                {
                    case "ping":
                    case "remote":
                        return RemoteCommands.Ping(cl);
                    case "doctor":
                        return RemoteCommands.Doctor(cl);
                    case "build":
                        return RemoteCommands.Build(cl);
                    case "deploy":
                        return RemoteCommands.Deploy(cl);
                    case "export":
                        return RemoteCommands.Export(cl);

                    // Windows-only verbs are not part of the remote surface.
                    case "install":
                    case "register":
                    case "reg":
                    case "unregister":
                    case "unreg":
                    case "uninstall":
                    case "serve":
                    case "service":
                    case "setup":
                    case "license":
                    case "provision":
                    case "pin":
                        Console.Error.WriteLine(
                            $"ERROR: '{command}' runs on fezd-server (Windows) only and is not available in fezd-client.");
                        return FezdExitCodes.UsageError;

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
