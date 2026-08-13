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
