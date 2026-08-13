using System;

namespace RevitCliBridge
{
    /// <summary>
    /// Pure-logic state machine for <see cref="TaskInfo"/> lifecycle transitions.
    /// Extracted from <see cref="TaskRegistry"/> so the transition rules
    /// (Pending → Running → Completed/Failed) and invariant enforcement
    /// can be unit-tested without the static <see cref="TaskRegistry"/> singleton.
    /// </summary>
    /// <remarks>
    /// All methods operate on a <see cref="TaskInfo"/> instance passed in by
    /// the caller and perform no global side effects. The caller owns the
    /// task lookup (e.g. <see cref="TaskRegistry"/> keeps the dictionary).
    /// </remarks>
    public static class TaskStateMachine
    {
        /// <summary>
        /// Transition a task from Pending to Running, stamp StartedAt, and
        /// broadcast a "progress" event with progress=0.
        /// No-op if the task is null (mirrors TaskRegistry.SetRunning).
        /// </summary>
        public static void SetRunning(TaskInfo? task)
        {
            if (task is null) return;

            task.Status = CliTaskStatus.Running;
            task.StartedAt = DateTime.Now;
            task.Broadcast("progress", new { task_id = task.TaskId, progress = 0, message = "Execution started" });
        }

        /// <summary>
        /// Update progress (0-100) and optional message on a Running task,
        /// broadcasting a "progress" event.
        /// No-op if the task is null.
        /// </summary>
        public static void SetProgress(TaskInfo? task, int progress, string? message = null)
        {
            if (task is null) return;

            task.Progress = progress;
            if (message is not null)
                task.ProgressMessage = message;
            task.Broadcast("progress", new { task_id = task.TaskId, progress, message });
        }

        /// <summary>
        /// Transition a task to Completed, stamp CompletedAt, cache resultJson,
        /// broadcast a "completed" event, and complete the TaskCompletionSource.
        /// No-op if the task is null. TrySetResult guards against double-completion.
        /// </summary>
        public static void SetCompleted(TaskInfo? task, string resultJson)
        {
            if (task is null) return;

            task.Status = CliTaskStatus.Completed;
            task.ResultJson = resultJson;
            task.CompletedAt = DateTime.Now;
            task.Broadcast("completed", new { task_id = task.TaskId, status = "completed", result = SafeParseJson(resultJson) });
            // Use TrySetResult to avoid InvalidOperationException if already set.
            task.Tcs.TrySetResult(resultJson);
        }

        /// <summary>
        /// Transition a task to Failed, stamp CompletedAt, cache errorJson,
        /// broadcast a "failed" event, and complete the TaskCompletionSource
        /// with the error payload.
        /// No-op if the task is null. TrySetResult guards against double-completion.
        /// </summary>
        public static void SetFailed(TaskInfo? task, string errorJson)
        {
            if (task is null) return;

            task.Status = CliTaskStatus.Failed;
            task.ResultJson = errorJson;
            task.CompletedAt = DateTime.Now;
            task.Broadcast("failed", new { task_id = task.TaskId, status = "failed", result = SafeParseJson(errorJson) });
            // Use TrySetResult to avoid InvalidOperationException if already set.
            task.Tcs.TrySetResult(errorJson);
        }

        /// <summary>
        /// Best-effort JSON parse for SSE broadcast payloads. Falls back to
        /// the raw string if parsing fails, so a non-JSON result doesn't
        /// break the event broadcast.
        /// </summary>
        private static object SafeParseJson(string json)
        {
            try { return Newtonsoft.Json.Linq.JObject.Parse(json); }
            catch { return json; }
        }
    }
}
