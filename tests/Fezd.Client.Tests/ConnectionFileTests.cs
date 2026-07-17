using System;
using System.IO;
using System.Text;
using Fezd.Client;
using Fezd.Contracts;
using Xunit;

namespace Fezd.Client.Tests
{
    public class ConnectionFileTests
    {
        [Fact]
        public void Parse_ReadsUrlAndToken()
        {
            string path = WriteTemp(
                "# comment\n" +
                "FEZD_URL=https://fezd.scadadog.com\n" +
                "FEZD_TOKEN=abc123\n");
            try
            {
                ConnectionFile c = ConnectionFile.Parse(path);
                Assert.Equal("https://fezd.scadadog.com", c.Url);
                Assert.Equal("abc123", c.Token);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Parse_AcceptsExportPrefixAndSpacesAroundEquals()
        {
            string path = WriteTemp(
                "export FEZD_URL = https://fezd.scadadog.com\n" +
                "export FEZD_TOKEN = \"tok\"\n");
            try
            {
                ConnectionFile c = ConnectionFile.Parse(path);
                Assert.Equal("https://fezd.scadadog.com", c.Url);
                Assert.Equal("tok", c.Token);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Parse_Utf8Bom_DoesNotBreakKeys()
        {
            string path = Path.Combine(Path.GetTempPath(), "fezd-bom-" + Guid.NewGuid().ToString("N") + ".fezd.env");
            byte[] body = Encoding.UTF8.GetBytes("FEZD_URL=https://fezd.scadadog.com\nFEZD_TOKEN=t\n");
            byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var all = new byte[bom.Length + body.Length];
            Buffer.BlockCopy(bom, 0, all, 0, bom.Length);
            Buffer.BlockCopy(body, 0, all, bom.Length, body.Length);
            File.WriteAllBytes(path, all);
            try
            {
                ConnectionFile c = ConnectionFile.Parse(path);
                Assert.Equal("https://fezd.scadadog.com", c.Url);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void LooksLikeConnectionPath_RecognizesFezdEnv()
        {
            Assert.True(ConnectionFile.LooksLikeConnectionPath("./remote-client.fezd.env"));
            Assert.True(ConnectionFile.LooksLikeConnectionPath(@"C:\FEZD\client.fezd.env"));
            Assert.False(ConnectionFile.LooksLikeConnectionPath("project.zef"));
            Assert.False(ConnectionFile.LooksLikeConnectionPath("session-id"));
        }

        [Fact]
        public void Parse_MissingFile_ThrowsUsage()
        {
            var ex = Assert.Throws<RemoteCommsException>(() =>
                ConnectionFile.Parse(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid() + ".fezd.env")));
            Assert.Equal(FezdExitCodes.UsageError, ex.ExitCode);
            Assert.Contains("not found", ex.Message);
        }

        private static string WriteTemp(string contents)
        {
            string path = Path.Combine(Path.GetTempPath(), "fezd-conn-" + Guid.NewGuid().ToString("N") + ".fezd.env");
            File.WriteAllText(path, contents);
            return path;
        }
    }
}
