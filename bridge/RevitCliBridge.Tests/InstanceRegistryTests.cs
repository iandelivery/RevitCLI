using Newtonsoft.Json;
using RevitCliBridge;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for the instance registry JSON contract around the api_key
    /// field: written when configured, omitted when absent, and tolerated
    /// as null when reading files produced by older bridges.
    /// </summary>
    public class InstanceRegistryTests
    {
        private static InstanceRegistry.InstanceInfo SampleInfo() => new InstanceRegistry.InstanceInfo
        {
            Pid = 44024,
            Version = 2019,
            Port = 5011,
            StartedAt = System.DateTime.UtcNow,
            Hostname = "localhost",
            CommandsCount = 99,
            ApiKey = "registry-key"
        };

        [Fact]
        public void Serialize_WithApiKey_WritesApiKeyField()
        {
            var json = JsonConvert.SerializeObject(SampleInfo());

            Assert.Contains("\"api_key\":\"registry-key\"", json);
        }

        [Fact]
        public void Serialize_WithoutApiKey_OmitsApiKeyField()
        {
            var info = SampleInfo();
            info.ApiKey = null;

            var json = JsonConvert.SerializeObject(info);

            Assert.DoesNotContain("api_key", json);
        }

        [Fact]
        public void Deserialize_RegistryFileWithoutApiKey_LeavesApiKeyNull()
        {
            const string json = @"{ ""pid"": 1, ""version"": 2019, ""port"": 5012, ""started_at"": ""2026-11-17T00:00:00Z"", ""hostname"": ""localhost"", ""commands_count"": 99 }";

            var info = JsonConvert.DeserializeObject<InstanceRegistry.InstanceInfo>(json);

            Assert.NotNull(info);
            Assert.Equal(5012, info!.Port);
            Assert.Null(info.ApiKey);
        }

        [Fact]
        public void Deserialize_RoundTripsApiKey()
        {
            var json = JsonConvert.SerializeObject(SampleInfo());

            var info = JsonConvert.DeserializeObject<InstanceRegistry.InstanceInfo>(json);

            Assert.NotNull(info);
            Assert.Equal("registry-key", info!.ApiKey);
        }
    }
}
