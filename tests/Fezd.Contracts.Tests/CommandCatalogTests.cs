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
            string[] localOnly = { "install", "register", "unregister", "serve", "setup", "license", "pin", "service" };
            foreach (string name in localOnly)
            {
                CommandInfo cmd = CommandCatalog.Find(name);
                Assert.NotNull(cmd);
                Assert.False(cmd.IsAvailableIn(remoteMode: true), name + " should be hidden from fezd-client");
                Assert.True(cmd.IsAvailableIn(remoteMode: false), name + " should appear on fezd-server");
            }
        }

        [Fact]
        public void ClientSurface_IncludesRemoteOps()
        {
            foreach (string name in new[] { "ping", "doctor", "build", "deploy", "export" })
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

            Assert.Contains("fezd-server", server);
            Assert.Contains("\n  serve", server);
            Assert.Contains("\n  setup", server);
            Assert.Contains("\n  license", server);
            Assert.DoesNotContain("\n  provision", server);
        }

        [Fact]
        public void Help_ServerDoesNotListRemoteGlobalOptions()
        {
            var meta = new AppMetadata { Version = "9.9.9", Product = "FEZD" };
            string server = HelpRenderer.RenderUsage(meta, remoteMode: false);
            string client = HelpRenderer.RenderUsage(meta, remoteMode: true);

            // Global remote options must not appear in server help (command text may still
            // mention --pin as the value clients use).
            Assert.DoesNotContain("--remote <url>", server);
            Assert.DoesNotContain("--connection <file>", server);
            Assert.DoesNotContain("--license <value>", server);
            Assert.DoesNotContain("--token <value>", server);
            Assert.DoesNotContain("--ca-cert <file>", server);

            Assert.Contains("--connection <file>", client);
            Assert.Contains("--remote <url>", client);
            Assert.Contains("--license <value>", client);
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
        }

        [Fact]
        public void Find_ResolvesAliases()
        {
            Assert.Equal("ping", CommandCatalog.Find("remote").Name);
            Assert.Equal("platforms", CommandCatalog.Find("plcs").Name);
        }
    }
}
