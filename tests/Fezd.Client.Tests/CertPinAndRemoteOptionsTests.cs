using System;
using Fezd.Client;
using Fezd.Contracts;
using Xunit;

namespace Fezd.Client.Tests
{
    public class CertPinTests
    {
        [Theory]
        [InlineData("sha256:AABBCcddee", "aabbccddee")]
        [InlineData("SHA-256:aa:bb:cc", "aabbcc")]
        [InlineData("Aa:Bb:Cc:Dd", "aabbccdd")]
        [InlineData("  deadbeef  ", "deadbeef")]
        public void Normalize_AcceptsServePrintedForms(string input, string expected) =>
            Assert.Equal(expected, CertPin.Normalize(input));

        [Fact]
        public void Normalize_NullStaysNull() =>
            Assert.Null(CertPin.Normalize(null));
    }

    public class RemoteFezdExecutorCtorTests
    {
        [Fact]
        public void Ctor_RequiresBaseUrl()
        {
            var ex = Assert.Throws<RemoteCommsException>(() =>
                new RemoteFezdExecutor(new RemoteOptions { Token = "t" }));
            Assert.Equal(FezdExitCodes.UsageError, ex.ExitCode);
            Assert.Contains("--remote", ex.Message);
        }

        [Fact]
        public void Ctor_RejectsNullOptions() =>
            Assert.Throws<ArgumentNullException>(() => new RemoteFezdExecutor(null));

        [Fact]
        public void Ctor_AcceptsHttpsEndpoint()
        {
            using var exec = new RemoteFezdExecutor(new RemoteOptions
            {
                BaseUrl = new Uri("https://gateway.example:8443/"),
                Token = "token",
                PinSha256 = "sha256:ab"
            });
            Assert.NotNull(exec);
        }
    }
}
