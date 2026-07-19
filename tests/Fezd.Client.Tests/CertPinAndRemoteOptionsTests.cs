using System;
using System.Net;
using Fezd.Client;
using Fezd.Contracts;
using Fezd.Contracts.Cli;
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

        [Fact]
        public void Ctor_AcceptsHttpsWithoutPin_SystemCaTrust()
        {
            // Corp / public-CA path: no FEZD_PIN; ValidateServerCert accepts SslPolicyErrors.None.
            using var exec = new RemoteFezdExecutor(new RemoteOptions
            {
                BaseUrl = new Uri("https://fezd.scadadog.app/"),
                Token = "token"
            });
            Assert.NotNull(exec);
        }
    }

    public class ProxyRouteDetectorTests
    {
        private static readonly Uri Destination = new Uri("https://gateway.example:8443/");

        [Fact]
        public void Detect_UsesConfiguredProxy()
        {
            var route = ProxyRouteDetector.Detect(
                Destination,
                noProxy: false,
                new FixedProxy(new Uri("http://user:secret@proxy.example:8080/"), bypassed: false));

            Assert.True(route.UsesProxy);
            Assert.Equal("proxy.example", route.ProxyUri.Host);
            Assert.Equal("proxy http://proxy.example:8080", route.Description);
        }

        [Fact]
        public void Detect_RespectsSystemBypass()
        {
            var route = ProxyRouteDetector.Detect(
                Destination,
                noProxy: false,
                new FixedProxy(new Uri("http://proxy.example:8080/"), bypassed: true));

            Assert.False(route.UsesProxy);
            Assert.Equal("direct", route.Description);
        }

        [Fact]
        public void Detect_NoProxyForcesDirectRoute()
        {
            var route = ProxyRouteDetector.Detect(
                Destination,
                noProxy: true,
                new FixedProxy(new Uri("http://proxy.example:8080/"), bypassed: false));

            Assert.False(route.UsesProxy);
            Assert.Null(route.ProxyUri);
        }

        [Fact]
        public void UploadChunkDefaults_AreLargerForDirectRoute()
        {
            Assert.Equal(1024, RemoteOptions.ProxiedUploadChunkKb);
            Assert.Equal(16 * 1024, RemoteOptions.DirectUploadChunkKb);
            Assert.True(RemoteOptions.DirectUploadChunkKb > RemoteOptions.ProxiedUploadChunkKb);
            Assert.Equal(0, new RemoteOptions().UploadChunkKb);
        }

        private sealed class FixedProxy : IWebProxy
        {
            private readonly Uri _proxy;
            private readonly bool _bypassed;

            public FixedProxy(Uri proxy, bool bypassed)
            {
                _proxy = proxy;
                _bypassed = bypassed;
            }

            public ICredentials Credentials { get; set; }
            public Uri GetProxy(Uri destination) => _proxy;
            public bool IsBypassed(Uri host) => _bypassed;
        }
    }

    public class RemoteCliGuardsTests
    {
        [Fact]
        public void Deploy_RejectsMode()
        {
            CommandLine cl = CommandLine.Parse(new[] { "deploy", "p.zef", "--mode", "secondary" });
            var ex = Assert.Throws<RemoteCommsException>(() =>
                RemoteCliGuards.EnsureDeployFlagsSupported(cl));
            Assert.Equal(FezdExitCodes.UsageError, ex.ExitCode);
            Assert.Contains("--mode", ex.Message);
        }

        [Fact]
        public void Deploy_RejectsNoDownload()
        {
            CommandLine cl = CommandLine.Parse(new[] { "deploy", "p.zef", "--no-download" });
            var ex = Assert.Throws<RemoteCommsException>(() =>
                RemoteCliGuards.EnsureDeployFlagsSupported(cl));
            Assert.Contains("--no-download", ex.Message);
        }

        [Fact]
        public void Deploy_AllowsDefaultFlags()
        {
            CommandLine cl = CommandLine.Parse(new[] { "deploy", "p.zef", "--run", "--force" });
            RemoteCliGuards.EnsureDeployFlagsSupported(cl);
        }

        [Fact]
        public void Transport_RejectsNonLoopbackHttp()
        {
            var ex = Assert.Throws<RemoteCommsException>(() =>
                RemoteCliGuards.EnsureSecureTransport(new Uri("http://gateway.example:8443/")));
            Assert.Contains("https://", ex.Message);
        }

        [Fact]
        public void Transport_AllowsLoopbackHttp()
        {
            RemoteCliGuards.EnsureSecureTransport(new Uri("http://127.0.0.1:8443/"));
            RemoteCliGuards.EnsureSecureTransport(new Uri("http://localhost:8443/"));
        }

        [Fact]
        public void Transport_AllowsHttps()
        {
            RemoteCliGuards.EnsureSecureTransport(new Uri("https://gateway.example:8443/"));
        }

        [Theory]
        [InlineData("https://127.0.0.1:8443/", true)]
        [InlineData("https://localhost:8443/", true)]
        [InlineData("https://[::1]:8443/", true)]
        [InlineData("https://gateway.example:8443/", false)]
        [InlineData("https://10.0.0.5:8443/", false)]
        public void IsLoopbackHost_DetectsLoopback(string url, bool expected)
        {
            Assert.Equal(expected, RemoteCliGuards.IsLoopbackHost(new Uri(url)));
        }
    }
}
