using Newtonsoft.Json;
using System.Collections.Generic;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Represents a command response returned to the CLI client.
    /// </summary>
    public class CommandResponse
    {
        [JsonProperty("task_id")]
        public string TaskId { get; set; } = string.Empty;

        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object? Data { get; set; }

        [JsonProperty("error_details", NullValueHandling = NullValueHandling.Ignore)]
        public string? ErrorDetails { get; set; }

        /// <summary>
        /// Descriptions of the Revit errors the failure preprocessor
        /// resolved during the transaction. Serialized only when at least
        /// one error was resolved, so clean responses keep their shape.
        /// </summary>
        [JsonProperty("resolved_failures", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<string>? ResolvedFailures { get; set; }

        public static CommandResponse Success(string taskId, object? data, string message = "Success",
            IReadOnlyList<string>? resolvedFailures = null) =>
            new CommandResponse { TaskId = taskId, Status = "success", Message = message, Data = data,
                ResolvedFailures = resolvedFailures };

        /// <summary>
        /// Success for a transaction that ran with failure collection.
        /// When <paramref name="resolvedFailures"/> is non-empty, the
        /// message gains a summary suffix and the response carries the
        /// top-level <c>resolved_failures</c> field; otherwise this is
        /// identical to <see cref="Success"/> and the data shape stays
        /// unchanged.
        /// </summary>
        public static CommandResponse SuccessWithResolvedFailures(
            string taskId, object? data, string message, IReadOnlyList<string>? resolvedFailures)
        {
            if (resolvedFailures is null || resolvedFailures.Count == 0)
                return Success(taskId, data, message);
            return Success(taskId, data,
                $"{message} Revit auto-resolved {resolvedFailures.Count} error(s), see resolved_failures.",
                resolvedFailures);
        }

        public static CommandResponse Error(string taskId, string message, string? errorDetails = null) =>
            new CommandResponse { TaskId = taskId, Status = "error", Message = message, ErrorDetails = errorDetails };

        public string ToJson() => JsonConvert.SerializeObject(this);
    }
}
