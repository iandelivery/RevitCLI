using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace RevitCliBridge
{
    public class TaskInfo
    {
        [JsonProperty("task_id")]
        public string TaskId { get; set; } = string.Empty;

        [JsonProperty("command")]
        public string Command { get; set; } = string.Empty;

        [JsonProperty("status")]
        public TaskStatus Status { get; set; } = TaskStatus.Pending;

        [JsonProperty("progress")]
        public int Progress { get; set; }

        [JsonProperty("progress_message")]
        public string? ProgressMessage { get; set; }

        [JsonProperty("result")]
        public string? ResultJson { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("started_at")]
        public DateTime? StartedAt { get; set; }

        [JsonProperty("completed_at")]
        public DateTime? CompletedAt { get; set; }

        [JsonIgnore]
        public TaskCompletionSource<string> Tcs { get; } = new();

        /// <summary>
        /// SSE event broadcast delegate.
        /// Sends updates to all open SSE connections.
        /// Parameters: event name, JSON data
        /// </summary>
        public event Action<string, string>? OnSseEvent;

        /// <summary>
        /// Broadcast SSE event to all subscribers.
        /// </summary>
        public void Broadcast(string eventName, object payload)
        {
            var json = JsonConvert.SerializeObject(payload);
            OnSseEvent?.Invoke(eventName, json);
        }

        /// <summary>
        /// Clear all SSE event subscribers.
        /// </summary>
        public void ClearSseSubscribers()
        {
            OnSseEvent = null;
        }
    }
}
