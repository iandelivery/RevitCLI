using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for PortAllocator.GetBasePort — the deterministic version→port
    /// mapping used by every bridge startup. The mapping is a public contract
    /// shared with the Go client, so changes here must be intentional.
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
    }
}
