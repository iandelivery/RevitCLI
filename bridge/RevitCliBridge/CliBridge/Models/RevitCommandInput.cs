using Newtonsoft.Json;
using System.Collections.Generic;

namespace RevitCliBridge.Models
{
    /// <summary>
    /// Represents a command request from CLI to Revit.
    /// </summary>
    public class RevitCommandInput
    {
        /// <summary>
        /// Unique task ID for tracking async execution.
        /// </summary>
        [JsonProperty("task_id")]
        public string TaskId { get; set; } = string.Empty;

        /// <summary>
        /// Command name to route to the appropriate handler.
        /// </summary>
        [JsonProperty("command")]
        public string Command { get; set; } = string.Empty;

        /// <summary>
        /// Command-specific parameters payload.
        /// </summary>
        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object>? Parameters { get; set; }

        /// <summary>
        /// Timeout override in seconds (default: 30).
        /// </summary>
        [JsonProperty("timeout_seconds", NullValueHandling = NullValueHandling.Ignore)]
        public int? TimeoutSeconds { get; set; }

        [JsonProperty("async", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Async { get; set; }

        /// <summary>
        /// When true, simulate execution and roll back transactions instead of committing.
        /// </summary>
        [JsonProperty("dry_run")]
        public bool DryRun { get; set; }
    }
}
