using Newtonsoft.Json;

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

        public static CommandResponse Success(string taskId, object? data, string message = "Success") =>
            new CommandResponse { TaskId = taskId, Status = "success", Message = message, Data = data };

        public static CommandResponse Error(string taskId, string message, string? errorDetails = null) =>
            new CommandResponse { TaskId = taskId, Status = "error", Message = message, ErrorDetails = errorDetails };

        public string ToJson() => JsonConvert.SerializeObject(this);
    }
}
