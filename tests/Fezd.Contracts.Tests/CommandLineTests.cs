using System.Linq;
using Fezd.Contracts.Cli;
using Xunit;

namespace Fezd.Contracts.Tests
{
    public class CommandLineTests
    {
        [Fact]
        public void Parse_PositionalsAndFlags()
        {
            CommandLine cl = CommandLine.Parse(new[] { "deploy", "proj.zef", "-v", "--run" });
            Assert.Equal(new[] { "deploy", "proj.zef" }, cl.Positionals.ToArray());
            Assert.True(cl.HasFlag("run"));
            Assert.True(cl.HasFlag("v"));
        }

        [Fact]
        public void Parse_KeyValueAndEqualsForm()
        {
            CommandLine cl = CommandLine.Parse(new[]
            {
                "ping",
                "--remote", "https://host:8443",
                "--token=secret",
                "--pin", "sha256:abc"
            });
            Assert.Equal("https://host:8443", cl.GetOption("remote"));
            Assert.Equal("secret", cl.GetOption("token"));
            Assert.Equal("sha256:abc", cl.GetOption("pin"));
        }

        [Fact]
        public void GetSwitch_TriState()
        {
            Assert.True(CommandLine.Parse(new[] { "--json" }).GetSwitch("json"));
            Assert.False(CommandLine.Parse(new[] { "--no-json" }).GetSwitch("json"));
            Assert.Null(CommandLine.Parse(new[] { "doctor" }).GetSwitch("json"));
        }

        [Fact]
        public void GetInt_FallsBackWhenMissingOrInvalid()
        {
            Assert.Equal(502, CommandLine.Parse(new[] { "--port", "502" }).GetInt("port", 0));
            Assert.Equal(8443, CommandLine.Parse(new[] { "--port", "nope" }).GetInt("port", 8443));
            Assert.Equal(8443, CommandLine.Parse(new string[0]).GetInt("port", 8443));
        }

        [Fact]
        public void Parse_TreatsQuestionMarkAsShortFlag()
        {
            CommandLine cl = CommandLine.Parse(new[] { "-?" });
            Assert.True(cl.HasFlag("?"));
            Assert.Empty(cl.Positionals);
        }

        [Fact]
        public void Parse_TreatsQuestionMarkPositional()
        {
            CommandLine cl = CommandLine.Parse(new[] { "?" });
            Assert.Equal("?", cl.Positionals[0]);
        }
    }
}
