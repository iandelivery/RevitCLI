using System;
using System.Linq;
using RevitCliBridge;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for <see cref="HttpPrefixes"/>.Compute — the loopback prefixes
    /// the bridge binds. Clients reach the bridge either by host name
    /// (localhost) or raw loopback IP (127.0.0.1); missing either prefix
    /// makes HTTP.sys reject the request with 400 "Invalid Hostname", which
    /// historically misdirected troubleshooting toward discovery/auth
    /// instead of the listener.
    /// </summary>
    public class HttpPrefixesTests
    {
        [Fact]
        public void Compute_BindsLocalhostAndLoopbackIp()
        {
            var prefixes = HttpPrefixes.Compute(5011);

            Assert.Equal(2, prefixes.Count);
            Assert.Contains("http://localhost:5011/", prefixes);
            Assert.Contains("http://127.0.0.1:5011/", prefixes);
        }

        [Fact]
        public void Compute_InterpolatesPort()
        {
            var prefixes = HttpPrefixes.Compute(5041);

            Assert.All(prefixes, p => Assert.EndsWith(":5041/", p));
            Assert.Equal(2, prefixes.Count(p => p.StartsWith("http://")));
        }

        [Fact]
        public void Compute_NeverBindsWildcardOrExternalHost()
        {
            var prefixes = HttpPrefixes.Compute(5000);

            Assert.All(prefixes, p =>
                Assert.DoesNotContain("http://+:", p));
            Assert.All(prefixes, p =>
                Assert.DoesNotContain("http://*:", p));
        }
    }
}
