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

        [Fact]
        public void ValidateToken_TrimsSurroundingWhitespaceBeforeCompare()
        {
            var config = new CliBridgeConfig { ApiKey = "key" };
            Assert.True(CliBridgeConfigAuth.Validate(config, "  key  "));
        }

        [Fact]
        public void ValidateToken_BearerPrefixWithExtraSpaces_StillExtractsToken()
        {
            var config = new CliBridgeConfig { ApiKey = "secret" };
            Assert.True(CliBridgeConfigAuth.Validate(config, "Bearer   secret"));
        }

        [Fact]
        public void ValidateToken_BearerPrefixOnly_NoToken_AfterTrimBecomesEmpty()
        {
            var config = new CliBridgeConfig { ApiKey = "secret" };
            Assert.False(CliBridgeConfigAuth.Validate(config, "Bearer "));
            Assert.False(CliBridgeConfigAuth.Validate(config, "Bearer"));
        }

        [Fact]
        public void ValidateToken_WhitespaceOnlyToken_RejectedWhenAuthEnabled()
        {
            var config = new CliBridgeConfig { ApiKey = "key" };
            Assert.False(CliBridgeConfigAuth.Validate(config, "   "));
        }

        [Fact]
        public void ValidateToken_DifferentLengthToken_Rejected()
        {
            var config = new CliBridgeConfig { ApiKey = "key" };
            Assert.False(CliBridgeConfigAuth.Validate(config, "keys"));
            Assert.False(CliBridgeConfigAuth.Validate(config, "ke"));
        }

        [Fact]
        public void ValidateToken_SameLengthDifferentChar_Rejected()
        {
            var config = new CliBridgeConfig { ApiKey = "abcd" };
            Assert.False(CliBridgeConfigAuth.Validate(config, "abce"));
            Assert.False(CliBridgeConfigAuth.Validate(config, "xbcd"));
        }

        [Fact]
        public void IsAuthEnabled_WithWhitespaceOnlyApiKey_ReturnsTrue()
        {
            Assert.True(CliBridgeConfigAuth.IsEnabled(new CliBridgeConfig { ApiKey = " " }));
        }

        [Fact]
        public void ValidateToken_WithWhitespaceOnlyApiKey_RejectsAllTokensAfterTrim()
        {
            var config = new CliBridgeConfig { ApiKey = " " };
            Assert.False(CliBridgeConfigAuth.Validate(config, " "));
            Assert.False(CliBridgeConfigAuth.Validate(config, "x"));
            Assert.False(CliBridgeConfigAuth.Validate(config, null));
        }

        [Fact]
        public void GenerateKey_HasMinimumEntropy()
        {
            string key = CliBridgeConfigAuth.GenerateKey();
            Assert.True(key.Length >= 40, $"Key too short: {key.Length}");
        }

        [Fact]
        public void GenerateKey_UsesUrlSafeAlphabet()
        {
            for (int i = 0; i < 50; i++)
            {
                string key = CliBridgeConfigAuth.GenerateKey();
                Assert.DoesNotContain('+', key);
                Assert.DoesNotContain('/', key);
                Assert.DoesNotContain('=', key);
            }
        }

        [Fact]
        public void GenerateKey_ManyIterations_AllUnique()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                string k = CliBridgeConfigAuth.GenerateKey();
                Assert.True(seen.Add(k), $"Duplicate key generated at iteration {i}");
            }
        }
    }
}
