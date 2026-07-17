using Fezd.Contracts.Updates;
using Xunit;

namespace Fezd.Contracts.Tests
{
    public class SemVerTests
    {
        [Theory]
        [InlineData("2.3.1", "2.3.0", true)]
        [InlineData("2.3.0", "2.3.1", false)]
        [InlineData("2.3.1", "2.3.1", false)]
        [InlineData("v2.4.0", "2.3.9", true)]
        [InlineData("2.3.1+git", "2.3.0", true)]
        [InlineData("3.0.0-beta", "2.9.9", true)]
        public void IsNewer_Works(string candidate, string current, bool expected) =>
            Assert.Equal(expected, SemVer.IsNewer(candidate, current));

        [Fact]
        public void Normalize_StripsPrefixAndMetadata()
        {
            Assert.Equal("2.3.1", SemVer.Normalize("v2.3.1"));
            Assert.Equal("2.3.1", SemVer.Normalize("2.3.1+abc"));
            Assert.Equal("2.3.1", SemVer.Normalize("2.3.1-beta.1"));
        }
    }

    public class Sha256SumsTests
    {
        [Fact]
        public void TryGetExpectedHash_ParsesGnuStyle()
        {
            const string sums =
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  fezd-client-linux-x64\n" +
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb *fezd-client-win-x64.zip\n";

            Assert.True(Sha256Sums.TryGetExpectedHash(sums, "fezd-client-linux-x64", out string h1));
            Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", h1);
            Assert.True(Sha256Sums.TryGetExpectedHash(sums, "fezd-client-win-x64.zip", out string h2));
            Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", h2);
        }
    }
}
