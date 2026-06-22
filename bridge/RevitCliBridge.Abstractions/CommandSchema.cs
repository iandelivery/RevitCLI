using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Describes a single command's metadata for the schema discovery endpoint.
    /// </summary>
    public class CommandDef
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string? Description { get; set; }

        [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)]
        public string? Category { get; set; }

        [JsonProperty("aliases", NullValueHandling = NullValueHandling.Ignore)]
        public string[]? Aliases { get; set; }

        [JsonProperty("domain_path", NullValueHandling = NullValueHandling.Ignore)]
        public string? DomainPath { get; set; }

        [JsonProperty("supports_dry_run")]
        public bool SupportsDryRun { get; set; }

        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)]
        public CommandParamSchema[]? Parameters { get; set; }

        [JsonProperty("examples", NullValueHandling = NullValueHandling.Ignore)]
        public string[]? Examples { get; set; }
    }

    /// <summary>
    /// Full schema response for GET /api/commands.
    /// </summary>
    public class CommandSchema
    {
        [JsonProperty("version")]
        public string Version { get; set; } = "2.0.0";

        [JsonProperty("fetched_at")]
        public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

        [JsonProperty("server_info", NullValueHandling = NullValueHandling.Ignore)]
        public ServerInfo? ServerInfo { get; set; }

        [JsonProperty("commands")]
        public List<CommandDef> Commands { get; set; } = new List<CommandDef>();
    }

    /// <summary>
    /// Server metadata included in the schema response.
    /// </summary>
    public class ServerInfo
    {
        [JsonProperty("bridge_version", NullValueHandling = NullValueHandling.Ignore)]
        public string? BridgeVersion { get; set; }

        [JsonProperty("host", NullValueHandling = NullValueHandling.Ignore)]
        public string? Host { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }

        [JsonProperty("plugins", NullValueHandling = NullValueHandling.Ignore)]
        public string[]? Plugins { get; set; }

        [JsonProperty("features", NullValueHandling = NullValueHandling.Ignore)]
        public ServerFeatures? Features { get; set; }
    }

    /// <summary>
    /// Feature flags advertised by the server.
    /// </summary>
    public class ServerFeatures
    {
        [JsonProperty("dry_run")]
        public bool DryRun { get; set; }

        [JsonProperty("execute_raw")]
        public bool ExecuteRaw { get; set; }

        [JsonProperty("output_formats", NullValueHandling = NullValueHandling.Ignore)]
        public string[]? OutputFormats { get; set; }
    }
}
