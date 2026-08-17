using System;
using RevitCliBridge.Models;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for <see cref="CliBridgeConfig"/>'s constructor defaults and
    /// JSON-mapped field names. Defaults are a security- and
    /// resource-critical contract: the bridge relies on sane
    /// <c>TimeoutSeconds</c>, <c>MaxCommandQueueSize</c>,
    /// <c>MaxRequestBodySizeBytes</c>, and <c>AllowUnsignedPlugins</c>
    /// values when the config file omits them.
    /// </summary>
    public class CliBridgeConfigDefaultsTests
    {
        [Fact]
        public void Constructor_Enabled_DefaultsTrue()
        {
            Assert.True(new CliBridgeConfig().Enabled);
        }

        [Fact]
        public void Constructor_Port_DefaultsToFiveThousand()
        {
            Assert.Equal(5000, new CliBridgeConfig().Port);
        }

        [Fact]
        public void Constructor_TimeoutSeconds_DefaultsTo180()
        {
            Assert.Equal(180, new CliBridgeConfig().TimeoutSeconds);
        }

        [Fact]
        public void Constructor_MaxCommandQueueSize_DefaultsTo100()
        {
            Assert.Equal(100, new CliBridgeConfig().MaxCommandQueueSize);
        }

        [Fact]
        public void Constructor_AllowRawExecution_DefaultsToFalse()
        {
            Assert.False(new CliBridgeConfig().AllowRawExecution);
        }

        [Fact]
        public void Constructor_AutoPort_DefaultsToTrue()
        {
            Assert.True(new CliBridgeConfig().AutoPort);
        }

        [Fact]
        public void Constructor_MaxRequestBodySizeBytes_DefaultsToTenMiB()
        {
            Assert.Equal(10L * 1024 * 1024, new CliBridgeConfig().MaxRequestBodySizeBytes);
        }

        [Fact]
        public void Constructor_ApiKey_DefaultsToNull()
        {
            Assert.Null(new CliBridgeConfig().ApiKey);
        }

        [Fact]
        public void Constructor_AllowUnsignedPlugins_DefaultsToFalse()
        {
            // Security default: unsigned plugin loading is OFF.
            Assert.False(new CliBridgeConfig().AllowUnsignedPlugins);
        }

        [Fact]
        public void Constructor_TrustedPublishers_DefaultsToNull()
        {
            Assert.Null(new CliBridgeConfig().TrustedPublishers);
        }

        [Fact]
        public void Constructor_SchemaVersion_DefaultsToNull()
        {
            Assert.Null(new CliBridgeConfig().SchemaVersion);
        }

        [Fact]
        public void Constructor_AllProperties_CanBeOverridden()
        {
            var c = new CliBridgeConfig
            {
                Enabled = false,
                Port = 5041,
                TimeoutSeconds = 60,
                MaxCommandQueueSize = 50,
                AllowRawExecution = true,
                AutoPort = false,
                MaxRequestBodySizeBytes = 1024,
                ApiKey = "key-1",
                AllowUnsignedPlugins = true,
                TrustedPublishers = new[] { "CN=MyCorp" },
                SchemaVersion = "1"
            };
            Assert.False(c.Enabled);
            Assert.Equal(5041, c.Port);
            Assert.Equal(60, c.TimeoutSeconds);
            Assert.Equal(50, c.MaxCommandQueueSize);
            Assert.True(c.AllowRawExecution);
            Assert.False(c.AutoPort);
            Assert.Equal(1024, c.MaxRequestBodySizeBytes);
            Assert.Equal("key-1", c.ApiKey);
            Assert.True(c.AllowUnsignedPlugins);
            Assert.Equal(new[] { "CN=MyCorp" }, c.TrustedPublishers);
            Assert.Equal("1", c.SchemaVersion);
        }

        // ---------- JsonExtensions.ToJson (indented) ----------

        [Fact]
        public void JsonExtensions_ToJson_ReturnsIndentedJson()
        {
            var c = new CliBridgeConfig { Port = 5041 };
            var json = c.ToJson();
            // Indented JSON contains newlines and 2-space indentation.
            Assert.Contains(Environment.NewLine, json);
            Assert.Contains("  \"", json);
            Assert.Contains("\"port\": 5041", json);
        }
    }
}
