using Newtonsoft.Json;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RevitCliBridge
{
    public class TaskInfo
    {
        private readonly object _lock = new();
        private CliTaskStatus _status = CliTaskStatus.Pending;
        private int _progress;
        private string? _progressMessage;
        private string? _resultJson;
        private DateTime? _startedAt;
        private DateTime? _completedAt;

        [JsonProperty("task_id")]
        public string TaskId { get; set; } = string.Empty;

        [JsonProperty("request_id")]
        public string? RequestId { get; set; }

        [JsonProperty("command")]
        public string Command { get; set; } = string.Empty;

        [JsonProperty("status")]
        public CliTaskStatus Status
        {
            get { lock (_lock) return _status; }
        }

        /// <summary>
        /// Moves the task to <paramref name="target"/> when no earlier transition ended it,
        /// and sends <paramref name="eventName"/> with <paramref name="payload"/> to the open
        /// SSE stream under the same lock, so no later frame can arrive before this one.
        /// Returns false when the task is already terminal, so the caller drops its result.
        /// </summary>
        public bool TrySetStatus(CliTaskStatus target, string eventName, object payload)
        {
            lock (_lock)
            {
                if (_status is CliTaskStatus.Completed or CliTaskStatus.Failed or CliTaskStatus.Timeout)
                    return false;
                _status = target;
                Broadcast(eventName, payload);
                return true;
            }
        }

        [JsonProperty("progress")]
        public int Progress
        {
            get { lock (_lock) return _progress; }
            set { lock (_lock) _progress = value; }
        }

        [JsonProperty("progress_message")]
        public string? ProgressMessage
        {
            get { lock (_lock) return _progressMessage; }
            set { lock (_lock) _progressMessage = value; }
        }

        [JsonProperty("result")]
        public string? ResultJson
        {
            get { lock (_lock) return _resultJson; }
            set { lock (_lock) _resultJson = value; }
        }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("started_at")]
        public DateTime? StartedAt
        {
            get { lock (_lock) return _startedAt; }
            set { lock (_lock) _startedAt = value; }
        }

        [JsonProperty("completed_at")]
        public DateTime? CompletedAt
        {
            get { lock (_lock) return _completedAt; }
            set { lock (_lock) _completedAt = value; }
        }

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
        /// Thread-safe: captures delegate to local variable before invocation.
        /// </summary>
        public void Broadcast(string eventName, object payload)
        {
            var json = JsonConvert.SerializeObject(payload);
            var handler = OnSseEvent;
            handler?.Invoke(eventName, json);
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
