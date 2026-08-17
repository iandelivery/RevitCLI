using System;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for PortAllocator — the deterministic version→port mapping
    /// (GetBasePort) and the runtime port allocation workflow (AllocatePort).
    /// GetBasePort is a public contract shared with the Go client; AllocatePort
    /// is the production startup's port-discovery routine.
    /// </summary>
    public class PortAllocatorTests
    {
        [Theory]
        [InlineData(2019, 5011)]
        [InlineData(2020, 5021)]
        [InlineData(2021, 5031)]
        [InlineData(2022, 5041)]
        public void GetBasePort_ReturnsVersionSpecificBase(int version, int expectedPort)
        {
            Assert.Equal(expectedPort, PortAllocator.GetBasePort(version));
        }

        [Theory]
        [InlineData(2023, 5051)]
        [InlineData(2024, 5061)]
        public void GetBasePort_ExtendsForFutureVersions(int version, int expectedPort)
        {
            // Future-proofing: the formula must keep working for versions
            // beyond what's currently shipped, so the client and bridge
            // stay in sync as Autodesk releases new Revit versions.
            Assert.Equal(expectedPort, PortAllocator.GetBasePort(version));
        }

        [Fact]
        public void GetBasePort_ProducesContiguousRanges()
        {
            // Each version should reserve a 10-port range with no overlap.
            int port2019 = PortAllocator.GetBasePort(2019);
            int port2020 = PortAllocator.GetBasePort(2020);
            int port2021 = PortAllocator.GetBasePort(2021);
            int port2022 = PortAllocator.GetBasePort(2022);

            Assert.Equal(10, port2020 - port2019);
            Assert.Equal(10, port2021 - port2020);
            Assert.Equal(10, port2022 - port2021);
        }

        [Theory]
        [InlineData(2018)]
        [InlineData(2017)]
        public void GetBasePort_ProducesDecreasingPortsForOlderVersions(int version)
        {
            // The formula is linear — older versions map below R2019.
            // Sanity check the algebra rather than asserting a specific port.
            Assert.True(PortAllocator.GetBasePort(version) < PortAllocator.GetBasePort(2019));
        }

        // ---------- AllocatePort workflow ----------

        /// <summary>
        /// Helper: try to bind a TcpListener on localhost at the given port
        /// without blocking. Disposes the listener before returning.
        /// </summary>
        private static bool TryBind(int port)
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

        /// <summary>
        /// Helper: hold a TcpListener open until disposed.
        /// </summary>
        private sealed class PortHolder : IDisposable
        {
            private readonly TcpListener _listener;
            public int Port { get; }
            public PortHolder(int port)
            {
                Port = port;
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
            }
            public void Dispose()
            {
                try { _listener.Stop(); } catch { }
            }
        }

        [Fact]
        public void AllocatePort_ReturnsBindablePort()
        {
            // The core contract: AllocatePort must return a port that the
            // caller can subsequently bind. Use a far-future Revit version
            // whose port range is unlikely to collide with anything.
            int farFutureVersion = 2099;
            int port = PortAllocator.AllocatePort(farFutureVersion, fallbackPort: 65500);

            Assert.InRange(port, 1, 65535);
            // The port was available when AllocatePort checked it (IsPortAvailable
            // opened and closed a listener). Verify we can re-open it.
            Assert.True(TryBind(port),
                $"AllocatePort returned {port} but it cannot be re-bound immediately");
        }

        [Fact]
        public void AllocatePort_AvoidsPortHeldByAnotherProcess()
        {
            // Bind the version's base port ourselves, then verify
            // AllocatePort returns a different port.
            int farFutureVersion = 2098;
            int basePort = PortAllocator.GetBasePort(farFutureVersion);

            using (var holder = new PortHolder(basePort))
            {
                int allocated = PortAllocator.AllocatePort(farFutureVersion, fallbackPort: 65500);
                Assert.NotEqual(basePort, allocated);
            }
        }

        [Fact]
        public void AllocatePort_AllBasePortsHeld_FallsBackToConfiguredPort()
        {
            // Hold all 10 ports in the version's range. AllocatePort must
            // then return the configured fallback (if free).
            int farFutureVersion = 2097;
            int basePort = PortAllocator.GetBasePort(farFutureVersion);
            int fallbackPort = 65501; // unlikely to be in the version range

            // Make sure the fallback is actually free first.
            if (!TryBind(fallbackPort))
                return; // skip test if environment can't bind fallback

            using var h1 = new PortHolder(basePort + 0);
            using var h2 = new PortHolder(basePort + 1);
            using var h3 = new PortHolder(basePort + 2);
            using var h4 = new PortHolder(basePort + 3);
            using var h5 = new PortHolder(basePort + 4);
            using var h6 = new PortHolder(basePort + 5);
            using var h7 = new PortHolder(basePort + 6);
            using var h8 = new PortHolder(basePort + 7);
            using var h9 = new PortHolder(basePort + 8);
            using var h10 = new PortHolder(basePort + 9);

            int allocated = PortAllocator.AllocatePort(farFutureVersion, fallbackPort);
            Assert.Equal(fallbackPort, allocated);
        }

        [Fact]
        public void AllocatePort_FallbackPortHeld_ReturnsEphemeralPort()
        {
            // When both the version range and the fallback are taken,
            // AllocatePort asks the OS for an ephemeral port. We can't
            // predict which port, but it must be in the valid range and
            // must be bindable.
            int farFutureVersion = 2096;
            int basePort = PortAllocator.GetBasePort(farFutureVersion);
            int fallbackPort = 65502;

            using var h1 = new PortHolder(basePort + 0);
            using var h2 = new PortHolder(basePort + 1);
            using var h3 = new PortHolder(basePort + 2);
            using var h4 = new PortHolder(basePort + 3);
            using var h5 = new PortHolder(basePort + 4);
            using var h6 = new PortHolder(basePort + 5);
            using var h7 = new PortHolder(basePort + 6);
            using var h8 = new PortHolder(basePort + 7);
            using var h9 = new PortHolder(basePort + 8);
            using var h10 = new PortHolder(basePort + 9);
            using var fb = new PortHolder(fallbackPort);

            int allocated = PortAllocator.AllocatePort(farFutureVersion, fallbackPort);

            Assert.InRange(allocated, 1, 65535);
            // Ephemeral ports returned by the OS are typically above 1024
            // and well outside our base/fallback range.
            Assert.True(allocated < basePort || allocated > basePort + 9,
                $"Ephemeral port {allocated} unexpectedly fell in the base range");
        }
    }
}
