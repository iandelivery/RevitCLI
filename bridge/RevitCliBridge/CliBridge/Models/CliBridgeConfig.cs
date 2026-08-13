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
        /// API key for authenticating CLI requests. When non-empty, all
        /// /api/* endpoints (except /api/health and /api/identity) require
        /// an "Authorization: Bearer &lt;api_key&gt;" header.
        /// When empty or null, authentication is disabled (legacy mode).
        /// Auto-generated on first startup if missing from config file.
        /// </summary>
        [JsonProperty("api_key", NullValueHandling = NullValueHandling.Ignore)]
        public string? ApiKey { get; set; }

        /// <summary>
        /// Whether unsigned plugins can be loaded from the CliBridgePlugins
        /// directory. When false (default), only DLLs with a valid Authenticode
        /// signature from a trusted publisher are loaded. Set to true for
        /// development environments where DLLs are self-compiled.
        /// </summary>
        [JsonProperty("allow_unsigned_plugins")]
        public bool AllowUnsignedPlugins { get; set; } = false;

        /// <summary>
        /// List of trusted publisher certificate subject CNs (e.g.
        /// "CN=Your Company Name"). When a plugin DLL is signed, its
        /// publisher must match one of these entries. Ignored when
        /// <see cref="AllowUnsignedPlugins"/> is true.
        /// Empty list = accept any valid signature.
        /// </summary>
        [JsonProperty("trusted_publishers", NullValueHandling = NullValueHandling.Ignore)]
        public string[]? TrustedPublishers { get; set; }

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

                    var configPath = ResolveConfigPath();
                    if (!File.Exists(configPath))
                    {
                        _config = new CliBridgeConfig();
                        EnsureApiKey(_config, configPath);
                        return _config;
                    }

                    var loadedConfig = JsonConvert.DeserializeObject<CliBridgeConfig>(File.ReadAllText(configPath));
                    _config = loadedConfig ?? new CliBridgeConfig();
                    EnsureApiKey(_config, configPath);
                    return _config;
                }
            }
        }

        /// <summary>
        /// Path to the configuration file. Resolved relative to the executing
        /// assembly directory: &lt;assemblyDir&gt;/.config/cli_bridge_setting.json
        /// </summary>
        public static string ResolveConfigPath()
        {
            return Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                ".config",
                "cli_bridge_setting.json");
        }

        /// <summary>
        /// Ensures the ApiKey is set. If it is null or empty, a random
        /// 32-byte URL-safe token is generated and persisted back to the
        /// config file so subsequent loads remain stable. Persistence failures
        /// are logged but do not block startup — the in-memory key is still
        /// used for the current session.
        /// </summary>
        private static void EnsureApiKey(CliBridgeConfig config, string configPath)
        {
            if (!string.IsNullOrEmpty(config.ApiKey))
                return;

            config.ApiKey = GenerateApiKey();
            try
            {
                PersistConfig(config, configPath);
                CliLogger.Info($"Generated API key and persisted to {configPath}");
            }
            catch (Exception ex)
            {
                CliLogger.Warn($"Generated API key but failed to persist config: {ex.Message}. " +
                               "Key will be used for this session only.");
            }
        }

        /// <summary>
        /// Generates a 32-byte random API key as a URL-safe Base64 string.
        /// </summary>
        public static string GenerateApiKey()
        {
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// Persists the config back to disk atomically (temp file + rename).
        /// </summary>
        private static void PersistConfig(CliBridgeConfig config, string configPath)
        {
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            var tmpPath = configPath + ".tmp";
            File.WriteAllText(tmpPath, json);
            if (File.Exists(configPath))
                File.Delete(configPath);
            File.Move(tmpPath, configPath);
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

        /// <summary>
        /// Returns true if API key authentication is active (api_key is set).
        /// </summary>
        public static bool IsAuthEnabled => !string.IsNullOrEmpty(Config.ApiKey);

        /// <summary>
        /// Validates a bearer token against the configured API key.
        /// Returns true if authentication is disabled OR the token matches.
        /// </summary>
        public static bool ValidateToken(string? bearerToken)
        {
            var key = Config.ApiKey;
            if (string.IsNullOrEmpty(key))
                return true; // Auth disabled

            if (string.IsNullOrEmpty(bearerToken))
                return false;

            // Strip "Bearer " prefix if present.
            var token = bearerToken!.Trim();
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = token.Substring("Bearer ".Length).Trim();

            return ConstantTimeEquals(token, key!);
        }

        /// <summary>
        /// Constant-time string comparison to mitigate timing attacks.
        /// </summary>
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
