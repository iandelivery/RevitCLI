using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace RevitCliBridge.Models
{
    /// <summary>
    /// Represents the configuration for the CLI bridge.
    /// </summary>
    public class CliBridgeConfig
    {
        /// <summary>
        /// Schema version for future config format migrations.
        /// Current version: 1.
        /// </summary>
        [JsonProperty("schema_version", NullValueHandling = NullValueHandling.Ignore)]
        public string? SchemaVersion { get; set; }

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
        /// Maximum size, in bytes, of an incoming HTTP request body.
        /// Requests larger than this are rejected with HTTP 413.
        /// Default: 10 MiB.
        /// </summary>
        [JsonProperty("max_request_body_size_bytes")]
        public long MaxRequestBodySizeBytes { get; set; } = 10L * 1024 * 1024;

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
            MaxRequestBodySizeBytes = 10L * 1024 * 1024;
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
        private static readonly object _lock = new();

        public static CliBridgeConfig Config
        {
            get
            {
                if (_config is not null)
                    return _config;

                lock (_lock)
                {
                    // Double-check after acquiring lock.
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

        /// <summary>
        /// Enable or disable raw execution at runtime without restarting the bridge.
        /// Thread-safe: uses the same lock as Config for safe publication.
        /// </summary>
        public static void SetAllowRawExecution(bool enabled)
        {
            lock (_lock)
            {
                Config.AllowRawExecution = enabled;
            }
        }
    }
}
