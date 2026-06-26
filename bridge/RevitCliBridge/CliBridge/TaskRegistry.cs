using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using Newtonsoft.Json.Linq;

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
                task.Status = CliTaskStatus.Running;
                task.StartedAt = DateTime.Now;
                task.Broadcast("progress", new { task_id = taskId, progress = 0, message = "Execution started" });
            }
        }

        public static void SetProgress(string taskId, int progress, string? message = null)
        {
            if (Tasks.TryGetValue(taskId, out var task))
            {
                task.Progress = progress;
                if (message != null)
                    task.ProgressMessage = message;
                task.Broadcast("progress", new { task_id = taskId, progress, message });
            }
        }

        public static void SetCompleted(string taskId, string resultJson)
        {
            if (Tasks.TryGetValue(taskId, out var task))
            {
                task.Status = CliTaskStatus.Completed;
                task.ResultJson = resultJson;
                task.CompletedAt = DateTime.Now;
                task.Broadcast("completed", new { task_id = taskId, status = "completed", result = SafeParseJson(resultJson) });
                // Use TrySetResult to avoid InvalidOperationException if already set.
                task.Tcs.TrySetResult(resultJson);
            }
        }

        public static void SetFailed(string taskId, string errorJson)
        {
            if (Tasks.TryGetValue(taskId, out var task))
            {
                task.Status = CliTaskStatus.Failed;
                task.ResultJson = errorJson;
                task.CompletedAt = DateTime.Now;
                task.Broadcast("failed", new { task_id = taskId, status = "failed", result = SafeParseJson(errorJson) });
                // Use TrySetResult to avoid InvalidOperationException if already set.
                task.Tcs.TrySetResult(errorJson);
            }
        }

        private static object SafeParseJson(string json)
        {
            try { return JObject.Parse(json); }
            catch { return json; }
        }

        public static void CleanupOldTasks(int maxAgeSeconds = 300)
        {
            var cutoff = DateTime.Now.AddSeconds(-maxAgeSeconds);
            foreach (var kvp in Tasks)
            {
                if (kvp.Value.CompletedAt.HasValue && kvp.Value.CompletedAt.Value < cutoff)
                {
                    Tasks.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
