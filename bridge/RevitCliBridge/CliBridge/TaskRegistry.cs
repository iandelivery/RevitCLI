using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge
{
    public static class TaskRegistry
    {
        public static ConcurrentDictionary<string, TaskInfo> Tasks { get; } = new();

        public static ConcurrentQueue<QueuedCommand> CommandQueue { get; } = new();

        public static ExternalEvent? RevitEvent { get; set; }

        public static TaskInfo CreateTask(string taskId, string command)
        {
            var taskInfo = new TaskInfo
            {
                TaskId = taskId,
                Command = command,
                Status = CliTaskStatus.Pending,
                CreatedAt = DateTime.Now
            };

            Tasks[taskId] = taskInfo;
            return taskInfo;
        }

        public static TaskInfo? GetTask(string taskId)
        {
            Tasks.TryGetValue(taskId, out var task);
            return task;
        }

        public static void SetRunning(string taskId)
        {
            if (Tasks.TryGetValue(taskId, out var task))
            {
                TaskStateMachine.SetRunning(task);
            }
        }

        public static void SetProgress(string taskId, int progress, string? message = null)
        {
            if (Tasks.TryGetValue(taskId, out var task))
            {
                TaskStateMachine.SetProgress(task, progress, message);
            }
        }

        public static void SetCompleted(string taskId, string resultJson)
        {
            if (Tasks.TryGetValue(taskId, out var task))
            {
                TaskStateMachine.SetCompleted(task, resultJson);
            }
        }

        public static void SetFailed(string taskId, string errorJson)
        {
            if (Tasks.TryGetValue(taskId, out var task))
            {
                TaskStateMachine.SetFailed(task, errorJson);
            }
        }

        public static void SetTimeout(string taskId, string errorJson)
        {
            if (Tasks.TryGetValue(taskId, out var task))
            {
                TaskStateMachine.SetTimeout(task, errorJson);
            }
        }

        /// <summary>
        /// Returns the live task submitted with the given request ID, or null.
        /// Used to deduplicate client retries that reuse the same X-Request-Id.
        /// The dedup window ends when CleanupOldTasks removes the task.
        /// </summary>
        public static TaskInfo? FindByRequestId(string requestId)
        {
            if (string.IsNullOrEmpty(requestId)) return null;
            foreach (var kvp in Tasks)
            {
                if (kvp.Value.RequestId == requestId)
                    return kvp.Value;
            }
            return null;
        }

        /// <summary>
        /// How long failed and timed-out tasks are kept after completion.
        /// Longer than the finished-task retention because agents poll late
        /// exactly when something went wrong.
        /// </summary>
        public const int FailedTaskRetentionSeconds = 3600;

        /// <summary>
        /// Removes finished tasks past their retention window. Completed tasks
        /// use task_retention_seconds from the bridge config; failed and
        /// timed-out tasks use <see cref="FailedTaskRetentionSeconds"/>.
        /// </summary>
        public static void CleanupOldTasks()
        {
            int completedAge = CliBridgeConfigLoader.Config.TaskRetentionSeconds;
            var cutoffCompleted = DateTime.Now.AddSeconds(-completedAge);
            var cutoffFailed = DateTime.Now.AddSeconds(-FailedTaskRetentionSeconds);

            foreach (var kvp in Tasks)
            {
                if (!kvp.Value.CompletedAt.HasValue) continue;
                var cutoff = kvp.Value.Status == CliTaskStatus.Failed || kvp.Value.Status == CliTaskStatus.Timeout
                    ? cutoffFailed
                    : cutoffCompleted;
                if (kvp.Value.CompletedAt.Value < cutoff)
                {
                    Tasks.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
