using Newtonsoft.Json;
using System;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Represents a queued command waiting for execution on the Revit main thread.
    /// </summary>
    public class QueuedCommand
    {
        /// <summary>
        /// Progress callback wired by the bridge just before dispatch.
        /// Null when the command executes outside the bridge (unit tests,
        /// standalone tools) — <see cref="ReportProgress"/> then no-ops.
        /// Never serialized.
        /// </summary>
        [JsonIgnore]
        public Action<int, string?>? ProgressReporter { get; set; }

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

        /// <summary>
        /// Report execution progress (0-100) with an optional human-readable
        /// message. The bridge wires the reporter to the task's SSE
        /// "progress" event before dispatch — the same channel built-in
        /// commands use — so CLI clients observe live updates. Report only
        /// intermediate values: the bridge broadcasts 0 when the command
        /// starts and a terminal event on completion.
        /// Call from inside <see cref="IBridgeCommand.Handle"/> on the Revit
        /// main thread; no-ops when no reporter is wired.
        /// </summary>
        public void ReportProgress(int percent, string? message = null)
            => ProgressReporter?.Invoke(percent, message);
    }
}
