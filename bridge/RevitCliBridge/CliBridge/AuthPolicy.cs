using System;
using System.Collections.Generic;
using RevitCliBridge.Models;

namespace RevitCliBridge
{
    /// <summary>
    /// Authentication policy for the HTTP API: decides which paths require a
    /// Bearer token. Public paths stay open so clients can discover and probe
    /// the bridge before presenting credentials.
    /// </summary>
    public static class AuthPolicy
    {
        private static readonly HashSet<string> PublicPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/api/health",
            "/api/identity",
            "/api/auth/status"
        };

        /// <summary>
        /// Returns true if the given path requires Bearer token authentication.
        /// Public paths (health, identity, auth status) are always open.
        /// Non-/api paths (e.g. static files) do not require auth either.
        /// </summary>
        public static bool IsAuthRequired(string path)
        {
            if (!CliBridgeConfigLoader.IsAuthEnabled)
                return false;
            if (PublicPaths.Contains(path))
                return false;
            return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
