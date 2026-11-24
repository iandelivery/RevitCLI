using System;

namespace RevitCliBridge
{
    /// <summary>
    /// Pure-logic state machine for <see cref="TaskInfo"/> lifecycle transitions.
    /// Extracted from <see cref="TaskRegistry"/> so the transition rules
    /// (Pending → Running → Completed/Failed/Timeout) and invariant enforcement
    /// can be unit-tested without the static <see cref="TaskRegistry"/> singleton.
    /// Terminal states are final: the first terminal transition wins and later
    /// transitions are ignored, so a timed-out task cannot flip back to completed.
    /// </summary>
    /// <remarks>
    /// All methods operate on a <see cref="TaskInfo"/> instance passed in by
    /// the caller and perform no global side effects. The caller owns the
    /// task lookup (e.g. <see cref="TaskRegistry"/> keeps the dictionary).
    /// </remarks>
    public static class TaskStateMachine
    {
        /// <summary>
        /// Reports whether the status is terminal (no further transitions allowed).
        /// </summary>
        public static bool IsTerminal(CliTaskStatus status)
            => status is CliTaskStatus.Completed or CliTaskStatus.Failed or CliTaskStatus.Timeout;

        /// <summary>
        /// Transition a task from Pending to Running, stamp StartedAt, and
        /// broadcast a "progress" event with progress=0.
        /// No-op if the task is null or already terminal.
        /// Returns false when the task is null or the transition was refused.
        /// </summary>
        public static bool SetRunning(TaskInfo? task)
        {
            if (task is null) return false;
            if (!task.TrySetStatus(CliTaskStatus.Running, "progress",
                    new { task_id = task.TaskId, progress = 0, message = "Execution started" })) return false;

            task.StartedAt = DateTime.Now;
            return true;
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
        /// No-op if the task is null or already terminal (the first terminal
        /// transition wins). TrySetResult guards against double-completion.
        /// </summary>
        public static void SetCompleted(TaskInfo? task, string resultJson)
        {
            if (task is null) return;
            if (!task.TrySetStatus(CliTaskStatus.Completed, "completed",
                    new { task_id = task.TaskId, status = "completed", result = SafeParseJson(resultJson) })) return;

            task.ResultJson = resultJson;
            task.CompletedAt = DateTime.Now;
            task.Tcs.TrySetResult(resultJson);
        }

        /// <summary>
        /// Transition a task to Failed, stamp CompletedAt, cache errorJson,
        /// broadcast a "failed" event, and complete the TaskCompletionSource
        /// with the error payload.
        /// No-op if the task is null or already terminal (the first terminal
        /// transition wins). TrySetResult guards against double-completion.
        /// </summary>
        public static void SetFailed(TaskInfo? task, string errorJson)
        {
            if (task is null) return;
            if (!task.TrySetStatus(CliTaskStatus.Failed, "failed",
                    new { task_id = task.TaskId, status = "failed", result = SafeParseJson(errorJson) })) return;

            task.ResultJson = errorJson;
            task.CompletedAt = DateTime.Now;
            task.Tcs.TrySetResult(errorJson);
        }

        /// <summary>
        /// Transition a task to Timeout, stamp CompletedAt, cache errorJson,
        /// broadcast a "failed" event (so SSE clients terminate promptly) with
        /// status "timeout", and complete the TaskCompletionSource with the
        /// error payload. No-op if the task is null or already terminal.
        /// </summary>
        public static void SetTimeout(TaskInfo? task, string errorJson)
        {
            if (task is null) return;
            if (!task.TrySetStatus(CliTaskStatus.Timeout, "failed",
                    new { task_id = task.TaskId, status = "timeout", result = SafeParseJson(errorJson) })) return;

            task.ResultJson = errorJson;
            task.CompletedAt = DateTime.Now;
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
