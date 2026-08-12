using System.IO;
using RevitCliBridge.Models;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for CliBridgeConfig API-key validation logic.
    /// Covers the security-critical path of the auth gate without
    /// touching the filesystem or the full config loader.
    /// </summary>
    public class CliBridgeConfigAuthTests
    {
        [Fact]
        public void ValidateToken_AcceptsMatchingBearerToken()
        {
            var config = new CliBridgeConfig { ApiKey = "secret-key-123" };
            Assert.True(CliBridgeConfigAuth.Validate(config, "Bearer secret-key-123"));
        }

        [Fact]
        public void ValidateToken_AcceptsRawTokenWithoutPrefix()
        {
            var config = new CliBridgeConfig { ApiKey = "secret-key-123" };
            Assert.True(CliBridgeConfigAuth.Validate(config, "secret-key-123"));
        }

        [Fact]
        public void ValidateToken_AcceptsCaseInsensitiveBearerPrefix()
        {
            var config = new CliBridgeConfig { ApiKey = "key" };
            Assert.True(CliBridgeConfigAuth.Validate(config, "bearer key"));
            Assert.True(CliBridgeConfigAuth.Validate(config, "BEARER key"));
        }

        [Fact]
        public void ValidateToken_RejectsWrongToken()
        {
            var config = new CliBridgeConfig { ApiKey = "right-key" };
            Assert.False(CliBridgeConfigAuth.Validate(config, "Bearer wrong-key"));
        }

        [Fact]
        public void ValidateToken_RejectsEmptyTokenWhenAuthEnabled()
        {
            var config = new CliBridgeConfig { ApiKey = "key" };
            Assert.False(CliBridgeConfigAuth.Validate(config, ""));
            Assert.False(CliBridgeConfigAuth.Validate(config, null));
        }

        [Fact]
        public void ValidateToken_AllowsEverythingWhenAuthDisabled()
        {
            // When api_key is null/empty, auth is disabled — any token passes.
            var config = new CliBridgeConfig { ApiKey = null };
            Assert.True(CliBridgeConfigAuth.Validate(config, null));
            Assert.True(CliBridgeConfigAuth.Validate(config, ""));
            Assert.True(CliBridgeConfigAuth.Validate(config, "anything"));
        }

        [Fact]
        public void GenerateApiKey_ProducesUrlSafeBase64WithoutPadding()
        {
            string key = CliBridgeConfigAuth.GenerateKey();
            Assert.True(key.Length >= 40 && key.Length <= 43);
            Assert.DoesNotContain('+', key);
            Assert.DoesNotContain('/', key);
            Assert.DoesNotContain('=', key);
        }

        [Fact]
        public void GenerateApiKey_ProducesUniqueValues()
        {
            string a = CliBridgeConfigAuth.GenerateKey();
            string b = CliBridgeConfigAuth.GenerateKey();
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void IsAuthEnabled_ReflectsApiKeyPresence()
        {
            Assert.False(CliBridgeConfigAuth.IsEnabled(new CliBridgeConfig { ApiKey = null }));
            Assert.False(CliBridgeConfigAuth.IsEnabled(new CliBridgeConfig { ApiKey = "" }));
            Assert.True(CliBridgeConfigAuth.IsEnabled(new CliBridgeConfig { ApiKey = "x" }));
        }
    }
}
