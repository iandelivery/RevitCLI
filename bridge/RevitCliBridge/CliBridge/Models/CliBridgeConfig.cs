using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;

namespace RevitCliBridge.Models
{
    /// <summary>
    /// Represents the configuration for the CLI bridge.
    /// </summary>
    public class CliBridgeConfig
    {
        /// <summary>
        /// Whether the CLI bridge is enabled.
        /// </summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        /// <summary>
        /// TCP port to listen on.
        /// </summary>
        [JsonProperty("port")]
        public int Port { get; set; }

        /// <summary>
        /// Timeout in seconds for operations.
        /// </summary>
        [JsonProperty("timeout_seconds")]
        public int TimeoutSeconds { get; set; }

        /// <summary>
        /// Maximum size of the command queue.
        /// </summary>
        [JsonProperty("max_command_queue_size")]
        public int MaxCommandQueueSize { get; set; }

        [JsonProperty("allow_raw_execution")]
        public bool AllowRawExecution { get; set; }

        /// <summary>
        /// Whether to auto-detect an available port based on the Revit version.
        /// When true, the bridge ignores the hardcoded Port value and uses PortAllocator instead.
        /// </summary>
        [JsonProperty("auto_port")]
        public bool AutoPort { get; set; } = true;

        /// <summary>
        /// Default values aligned with `cli_bridge_setting.json`.
        /// </summary>
        public CliBridgeConfig()
        {
            Enabled = true;
            Port = 5000;
            TimeoutSeconds = 180;
            MaxCommandQueueSize = 100;
            AllowRawExecution = false;
            AutoPort = true;
        }
    }

    /// <summary>
    /// Extension methods for serializing command objects to JSON.
    /// </summary>
    public static class JsonExtensions
    {
        public static string ToJson(this object obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented);
        }
    }

    /// <summary>
    /// Loads CLI bridge configuration from JSON file.
    /// </summary>
    public static class CliBridgeConfigLoader
    {
        private static CliBridgeConfig? _config;

        public static CliBridgeConfig Config
        {
            get
            {
                if (_config is not null)
                    return _config;

                var configPath = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    ".config",
                    "cli_bridge_setting.json");

                if (!File.Exists(configPath))
                {
                    _config = new CliBridgeConfig();
                    return _config;
                }

                var loadedConfig = JsonConvert.DeserializeObject<CliBridgeConfig>(File.ReadAllText(configPath));
                _config = loadedConfig ?? new CliBridgeConfig();
                return _config;
            }
        }
    }
}
