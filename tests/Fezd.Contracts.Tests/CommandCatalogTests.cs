using System;
using System.Linq;
using Fezd.Contracts.Cli;
using Xunit;

namespace Fezd.Contracts.Tests
{
    public class CommandCatalogTests
    {
        [Fact]
        public void RemoteMode_HidesServerOnlyVerbs()
        {
            string[] localOnly =
            {
                "install", "register", "unregister", "disconnect", "inspect",
                "serve", "setup", "license", "pin", "service"
            };
            foreach (string name in localOnly)
            {
                CommandInfo cmd = CommandCatalog.Find(name);
                Assert.NotNull(cmd);
                Assert.False(cmd.IsAvailableIn(remoteMode: true), name + " should be hidden from fezd-client");
                Assert.True(cmd.IsAvailableIn(remoteMode: false), name + " should appear on fezd-server");
                Assert.True(CommandCatalog.IsHostOnlyVerb(name));
            }

            Assert.True(CommandCatalog.IsHostOnlyVerb("reg"));
            Assert.True(CommandCatalog.IsHostOnlyVerb("provision"));
            Assert.False(CommandCatalog.IsHostOnlyVerb("deploy"));
            Assert.False(CommandCatalog.IsHostOnlyVerb("cancel"));
        }

        [Fact]
        public void ClientSurface_IncludesRemoteOps()
        {
            foreach (string name in new[] { "ping", "doctor", "build", "deploy", "export", "cancel" })
            {
                CommandInfo cmd = CommandCatalog.Find(name);
                Assert.NotNull(cmd);
                Assert.True(cmd.IsAvailableIn(remoteMode: true), name);
            }
        }

        [Fact]
        public void Ping_IsRemoteOnly()
        {
            CommandInfo ping = CommandCatalog.Find("ping");
            Assert.NotNull(ping);
            Assert.Equal(CommandAvailability.Remote, ping.Availability);
            Assert.True(ping.IsAvailableIn(remoteMode: true));
            Assert.False(ping.IsAvailableIn(remoteMode: false));
        }

        [Fact]
        public void Cancel_IsRemoteOnly()
        {
            CommandInfo cancel = CommandCatalog.Find("cancel");
            Assert.NotNull(cancel);
            Assert.Equal(CommandAvailability.Remote, cancel.Availability);
            Assert.True(cancel.IsAvailableIn(remoteMode: true));
            Assert.False(cancel.IsAvailableIn(remoteMode: false));
        }

        [Fact]
        public void Help_ClientDoesNotAdvertiseServe()
        {
            var meta = new AppMetadata { Version = "9.9.9", Product = "FEZD" };
            string client = HelpRenderer.RenderUsage(meta, remoteMode: true);
            string server = HelpRenderer.RenderUsage(meta, remoteMode: false);

            Assert.Contains("fezd-client", client);
            Assert.Contains("fezd-client <command>", client);
            Assert.DoesNotContain("\n  serve", client);
            Assert.DoesNotContain("\n  provision", client);
            Assert.DoesNotContain("\n  service", client);
            Assert.DoesNotContain("\n  setup", client);
            Assert.DoesNotContain("\n  license", client);
            Assert.DoesNotContain("\n  install", client);
            Assert.DoesNotContain("\n  register", client);
            Assert.DoesNotContain("\n  unregister", client);
            Assert.DoesNotContain("\n  disconnect", client);
            Assert.DoesNotContain("\n  inspect", client);

            Assert.Contains("fezd-server", server);
            Assert.Contains("\n  serve", server);
            Assert.Contains("\n  setup", server);
            Assert.Contains("\n  license", server);
            Assert.Contains("\n  disconnect", server);
            Assert.Contains("\n  inspect", server);
            Assert.DoesNotContain("\n  provision", server);
            Assert.DoesNotContain("\n  cancel", server);
        }

        [Fact]
        public void Help_ServerDoesNotListRemoteGlobalOptions()
        {
            var meta = new AppMetadata { Version = "9.9.9", Product = "FEZD" };
            string server = HelpRenderer.RenderUsage(meta, remoteMode: false);
            string client = HelpRenderer.RenderUsage(meta, remoteMode: true);

            Assert.DoesNotContain("--remote <url>", server);
            Assert.DoesNotContain("--connection <file>", server);
            Assert.DoesNotContain("--license <value>", server);
            Assert.DoesNotContain("--token <value>", server);
            Assert.DoesNotContain("--ca-cert <file>", server);
            Assert.DoesNotContain("--remote-timeout <sec>", server);

            Assert.Contains("--connection <file>", client);
            Assert.Contains("--remote <url>", client);
            Assert.Contains("--license <value>", client);
            Assert.Contains("--remote-timeout <sec>", client);
        }

        [Fact]
        public void Help_ClientDoesNotAdvertiseHostOnlyGlobalOptions()
        {
            var meta = new AppMetadata { Version = "9.9.9", Product = "FEZD" };
            string client = HelpRenderer.RenderUsage(meta, remoteMode: true);
            string server = HelpRenderer.RenderUsage(meta, remoteMode: false);

            string[] hostOnly =
            {
                "--config <path>",
                "--com-timeout <sec>",
                "--log-level <level>",
                "--verbose, -v",
                "--json | --no-json",
            };
            foreach (string opt in hostOnly)
            {
                Assert.DoesNotContain(opt, client);
                Assert.Contains(opt, server);
            }

            Assert.Contains("--debug", client);
            Assert.Contains("--trace", client);
            Assert.Contains("--debug", server);
            Assert.Contains("--com-timeout <sec>", server);
        }

        [Fact]
        public void Help_ClientHidesLocalOnlyDeployMode()
        {
            var meta = new AppMetadata { Version = "9.9.9", Product = "FEZD" };
            string client = HelpRenderer.RenderUsage(meta, remoteMode: true);
            string server = HelpRenderer.RenderUsage(meta, remoteMode: false);

            Assert.DoesNotContain("--mode primary|secondary", client);
            Assert.Contains("--mode primary|secondary", server);
            Assert.Contains("--build / --no-build", client);
            Assert.Contains("--test-project <path>", client);
            Assert.Contains("gateway environment", client);
            Assert.DoesNotContain("via the UDE broker", HelpRenderer.RenderPlatforms());
            Assert.Contains("Windows gateway", HelpRenderer.RenderPlatforms());
        }

        [Fact]
        public void Help_RendersOptionsFromCatalog()
        {
            CommandInfo deploy = CommandCatalog.Find("deploy");
            string[] clientDetails = HelpRenderer.ComposeDetailLines(deploy, remoteMode: true);
            string[] serverDetails = HelpRenderer.ComposeDetailLines(deploy, remoteMode: false);

            Assert.Contains(clientDetails, l => l.StartsWith("Options:", StringComparison.Ordinal));
            Assert.Contains(clientDetails, l => l.Contains("--app-password <pwd>"));
            Assert.DoesNotContain(clientDetails, l => l.Contains("--mode primary|secondary"));
            Assert.Contains(serverDetails, l => l.Contains("--mode primary|secondary"));
        }

        [Fact]
        public void Help_ClientExamplesPreferConnectionFile()
        {
            Assert.NotEmpty(CommandCatalog.ClientExamples);
            Assert.All(CommandCatalog.ClientExamples, ex =>
                Assert.Contains("--connection", ex));
            Assert.DoesNotContain(CommandCatalog.ServerExamples, ex => ex.Contains("--remote"));
            Assert.Contains(CommandCatalog.ServerExamples, ex =>
                ex.StartsWith("setup --hostname", StringComparison.Ordinal));
            Assert.DoesNotContain(CommandCatalog.ServerExamples, ex => ex.Contains("0.0.0.0"));
            Assert.Contains(CommandCatalog.ClientExamples, ex =>
                ex.StartsWith("cancel ", StringComparison.Ordinal));
        }

        [Fact]
        public void Find_ResolvesAliases()
        {
            Assert.Equal("ping", CommandCatalog.Find("remote").Name);
            Assert.Equal("platforms", CommandCatalog.Find("plcs").Name);
        }

        [Fact]
        public void HostOnlyVerb_CoversEveryLocalOnlyCatalogEntry()
        {
            foreach (CommandInfo cmd in CommandCatalog.Commands.Where(c =>
                c.Availability == CommandAvailability.LocalOnly))
            {
                Assert.True(CommandCatalog.IsHostOnlyVerb(cmd.Name), cmd.Name);
                foreach (string alias in cmd.Aliases)
                    Assert.True(CommandCatalog.IsHostOnlyVerb(alias), alias);
            }
        }
    }
}
