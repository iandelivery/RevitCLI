using System;
using System.Security.Cryptography;
using RevitCliBridge.Models;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Test-friendly wrapper around CliBridgeConfigLoader's auth logic.
    /// The production ValidateToken/IsAuthEnabled/GenerateApiKey methods
    /// are static and read from the global Config singleton, making them
    /// hard to unit-test in isolation. This wrapper exposes the same
    /// pure logic with explicit inputs so tests can cover every branch.
    ///
    /// If the production API ever accepts a config parameter directly,
    /// this wrapper can be removed and tests updated to call the
    /// production methods.
    /// </summary>
    internal static class CliBridgeConfigAuth
    {
        public static bool IsEnabled(CliBridgeConfig config)
            => !string.IsNullOrEmpty(config.ApiKey);

        public static bool Validate(CliBridgeConfig config, string? bearerToken)
        {
            var key = config.ApiKey;
            if (string.IsNullOrEmpty(key))
                return true; // Auth disabled

            if (string.IsNullOrEmpty(bearerToken))
                return false;

            var token = bearerToken!.Trim();
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = token.Substring("Bearer ".Length).Trim();

            return ConstantTimeEquals(token, key!);
        }

        public static string GenerateKey()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a is null || b is null) return ReferenceEquals(a, b);
            if (a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
