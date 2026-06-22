using System;
using System.Net;

namespace RevitCliBridge.Models
{
    /// <summary>
    /// Allocates ports for bridge instances based on Revit version.
    /// Port ranges: 2019→5001-5009, 2020→5011-5019, 2021→5021-5029, 2022→5031-5039
    /// Falls back to 5040-5049 for unknown versions.
    /// </summary>
    public static class PortAllocator
    {
        /// <summary>
        /// Compute the base port for a given Revit version year.
        /// </summary>
        public static int GetBasePort(int revitVersion)
        {
            // 2019 -> 5001, 2020 -> 5011, 2021 -> 5021, 2022 -> 5031
            int offset = (revitVersion - 2018) * 10 + 1;
            if (offset < 1 || offset > 50)
                offset = 49; // fallback range 5040-5049
            return 5000 + offset;
        }

        /// <summary>
        /// Find the first available port in the version's range.
        /// Probes basePort..basePort+9 by attempting to bind a temporary HttpListener.
        /// </summary>
        public static int FindAvailablePort(int revitVersion)
        {
            int basePort = GetBasePort(revitVersion);
            for (int port = basePort; port < basePort + 10; port++)
            {
                if (IsPortAvailable(port))
                    return port;
            }
            // All ports in range taken, try the fallback range
            for (int port = 5040; port < 5050; port++)
            {
                if (IsPortAvailable(port))
                    return port;
            }
            // Last resort: return the configured port
            return -1;
        }

        private static bool IsPortAvailable(int port)
        {
            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();
                listener.Stop();
                listener.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
