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

        /// <summary>
        /// End-to-end request tracing ID propagated from the HTTP
        /// <c>X-Request-Id</c> header (or minted by the server). Carried
        /// through the queue so handlers and logs can correlate a command
        /// execution back to the originating HTTP request. Empty for
        /// internally generated commands (e.g. batch sub-commands that
        /// inherit the parent's ID).
        /// </summary>
        [JsonProperty("request_id", NullValueHandling = NullValueHandling.Ignore)]
        public string? RequestId { get; set; }
    }
}
