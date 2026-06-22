using Newtonsoft.Json;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Represents a queued command waiting for execution on the Revit main thread.
    /// </summary>
    public class QueuedCommand
    {
        [JsonProperty("task_id")]
        public string TaskId { get; set; } = string.Empty;

        [JsonProperty("command")]
        public string Command { get; set; } = string.Empty;

        [JsonProperty("parameters")]
        public object? Parameters { get; set; }

        /// <summary>
        /// When true, the command handler should simulate execution and roll back
        /// any transactions instead of committing. The response will describe what
        /// would have happened without making permanent changes.
        /// </summary>
        [JsonProperty("dry_run")]
        public bool DryRun { get; set; }
    }
}
