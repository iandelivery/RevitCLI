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

        // ---------- Edge cases: token whitespace and prefix handling ----------

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
            // "Bearer " → after prefix strip and trim → "" → constant-time
            // compare against the configured key returns false.
            var config = new CliBridgeConfig { ApiKey = "secret" };
            Assert.False(CliBridgeConfigAuth.Validate(config, "Bearer "));
            Assert.False(CliBridgeConfigAuth.Validate(config, "Bearer"));
        }

        [Fact]
        public void ValidateToken_WhitespaceOnlyToken_RejectedWhenAuthEnabled()
        {
            // Trim converts "   " → "" → empty after the bearer prefix logic.
            var config = new CliBridgeConfig { ApiKey = "key" };
            Assert.False(CliBridgeConfigAuth.Validate(config, "   "));
        }

        // ---------- Edge cases: constant-time comparison ----------

        [Fact]
        public void ValidateToken_DifferentLengthToken_Rejected()
        {
            // ConstantTimeEquals short-circuits on length mismatch.
            var config = new CliBridgeConfig { ApiKey = "key" };
            Assert.False(CliBridgeConfigAuth.Validate(config, "keys"));
            Assert.False(CliBridgeConfigAuth.Validate(config, "ke"));
        }

        [Fact]
        public void ValidateToken_SameLengthDifferentChar_Rejected()
        {
            // ConstantTimeEquals returns false when same length but any
            // byte differs.
            var config = new CliBridgeConfig { ApiKey = "abcd" };
            Assert.False(CliBridgeConfigAuth.Validate(config, "abce"));
            Assert.False(CliBridgeConfigAuth.Validate(config, "xbcd"));
        }

        // ---------- Edge cases: API key edge values ----------

        [Fact]
        public void IsAuthEnabled_WithWhitespaceOnlyApiKey_ReturnsTrue()
        {
            // IsEnabled uses string.IsNullOrEmpty, which is false for
            // whitespace-only strings — so " " counts as enabled. This
            // is intentional: a configured key (even whitespace) means
            // auth is on; callers should configure empty as null.
            Assert.True(CliBridgeConfigAuth.IsEnabled(new CliBridgeConfig { ApiKey = " " }));
        }

        [Fact]
        public void ValidateToken_WithWhitespaceOnlyApiKey_RejectsAllTokensAfterTrim()
        {
            // When ApiKey is " ", IsEnabled returns true (not null/empty).
            // But every bearer token gets trimmed before comparison, so
            // a token " " becomes "" — which never equals " ". The auth
            // gate is effectively unusable, but the contract is consistent:
            // enabled keys require exact-match post-trim.
            var config = new CliBridgeConfig { ApiKey = " " };
            Assert.False(CliBridgeConfigAuth.Validate(config, " ")); // trims to ""
            Assert.False(CliBridgeConfigAuth.Validate(config, "x"));
            Assert.False(CliBridgeConfigAuth.Validate(config, null));
        }

        // ---------- Edge cases: GenerateKey properties ----------

        [Fact]
        public void GenerateKey_HasMinimumEntropy()
        {
            // 32 random bytes → ~256 bits of entropy. After base64
            // encoding (with padding stripped), length is roughly 43 chars.
            string key = CliBridgeConfigAuth.GenerateKey();
            Assert.True(key.Length >= 40, $"Key too short: {key.Length}");
        }

        [Fact]
        public void GenerateKey_UsesUrlSafeAlphabet()
        {
            // URL-safe base64 uses '-' and '_' instead of '+' and '/'.
            // Run several iterations to increase confidence (each
            // generate is independent; some 32-byte buffers may not
            // contain any '+' or '/' chars to substitute).
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
