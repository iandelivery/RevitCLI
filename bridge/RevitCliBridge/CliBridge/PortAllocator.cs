using System;
using System.Net;
using System.Net.Sockets;

namespace RevitCliBridge
{
    /// <summary>
    /// Allocates ports for bridge instances based on Revit version.
    /// Port scheme: base_port = 5000 + (version - 2018) * 10 + 1
    ///   Revit 2019 → 5011-5019
    ///   Revit 2020 → 5021-5029
    ///   Revit 2021 → 5031-5039
    ///   Revit 2022 → 5041-5049
    /// Port 5000 is reserved as a legacy fallback.
    /// </summary>
    public static class PortAllocator
    {
        private const int PortRangeSize = 10;

        /// <summary>
        /// Compute the base port for a given Revit version year.
        /// </summary>
        public static int GetBasePort(int revitVersion)
        {
            // Revit 2019 → 5011, 2020 → 5021, etc.
            return 5000 + (revitVersion - 2018) * 10 + 1;
        }

        /// <summary>
        /// Find the first available port in the version's port range.
        /// Returns the configured fallback port if no port in range is available.
        /// </summary>
        /// <param name="revitVersion">Revit version year (e.g. 2022)</param>
        /// <param name="fallbackPort">Port to use if auto-allocation fails (from config)</param>
        /// <returns>An available port number</returns>
        public static int AllocatePort(int revitVersion, int fallbackPort)
        {
            int basePort = GetBasePort(revitVersion);

            for (int offset = 0; offset < PortRangeSize; offset++)
            {
                int candidatePort = basePort + offset;
                if (IsPortAvailable(candidatePort))
                {
                    CliLogger.Info($"Auto-allocated port {candidatePort} for Revit {revitVersion}");
                    return candidatePort;
                }
                CliLogger.Info($"Port {candidatePort} is in use, trying next...");
            }

            // Fallback: try the configured port.
            if (IsPortAvailable(fallbackPort))
            {
                CliLogger.Info($"Version range exhausted, using configured port {fallbackPort}");
                return fallbackPort;
            }

            // Last resort: find any available port from the OS.
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int ephemeralPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                CliLogger.Warn($"All preferred ports in use, using ephemeral port {ephemeralPort}");
                return ephemeralPort;
            }
            catch (Exception ex)
            {
                CliLogger.Error($"Failed to allocate ephemeral port: {ex.Message}");
                return fallbackPort;
            }
        }

        /// <summary>
        /// Check if a port is available for binding on localhost.
        /// </summary>
        private static bool IsPortAvailable(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
