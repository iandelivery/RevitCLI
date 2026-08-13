using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for TaskStateMachine — the pure-logic task lifecycle transitions
    /// extracted from TaskRegistry. Verifies status changes, timestamp
    /// stamping, TCS completion, and SSE broadcast invocation.
    /// </summary>
    public class TaskStateMachineTests
    {
        private static TaskInfo NewTask(string id = "task-1", string command = "create_wall")
            => new TaskInfo { TaskId = id, Command = command, Status = CliTaskStatus.Pending };

        [Fact]
        public void SetRunning_TransitionsToRunningAndStampsStartedAt()
        {
            var task = NewTask();
            Assert.Equal(CliTaskStatus.Pending, task.Status);
            Assert.Null(task.StartedAt);

            TaskStateMachine.SetRunning(task);

            Assert.Equal(CliTaskStatus.Running, task.Status);
            Assert.NotNull(task.StartedAt);
        }

        [Fact]
        public void SetRunning_BroadcastsProgressZeroEvent()
        {
            var task = NewTask();
            string? seenEvent = null;
            string? seenJson = null;
            task.OnSseEvent += (name, json) => { seenEvent = name; seenJson = json; };

            TaskStateMachine.SetRunning(task);

            Assert.Equal("progress", seenEvent);
            Assert.Contains("\"progress\":0", seenJson);
            Assert.Contains("\"message\":\"Execution started\"", seenJson);
        }

        [Fact]
        public void SetRunning_NoOpOnNullTask()
        {
            // Should not throw.
            TaskStateMachine.SetRunning(null);
        }

        [Fact]
        public void SetProgress_UpdatesProgressAndMessage()
        {
            var task = NewTask();
            TaskStateMachine.SetRunning(task);

            TaskStateMachine.SetProgress(task, 50, "halfway");

            Assert.Equal(50, task.Progress);
            Assert.Equal("halfway", task.ProgressMessage);
        }

        [Fact]
        public void SetProgress_BroadcastsProgressEvent()
        {
            var task = NewTask();
            int seenProgress = -1;
            task.OnSseEvent += (name, json) =>
            {
                if (name == "progress" && json.Contains("\"progress\":75"))
                    seenProgress = 75;
            };

            TaskStateMachine.SetProgress(task, 75, "almost done");

            Assert.Equal(75, seenProgress);
        }

        [Fact]
        public void SetProgress_NoOpOnNullTask()
        {
            TaskStateMachine.SetProgress(null, 50, "msg");
        }

        [Fact]
        public void SetProgress_NullMessageDoesNotOverwriteExisting()
        {
            var task = NewTask();
            TaskStateMachine.SetProgress(task, 30, "first");

            TaskStateMachine.SetProgress(task, 60, null);

            Assert.Equal(60, task.Progress);
            Assert.Equal("first", task.ProgressMessage);
        }

        [Fact]
        public void SetCompleted_TransitionsAndStampsAndCompletesTcs()
        {
            var task = NewTask();
            var json = "{\"status\":\"ok\"}";

            TaskStateMachine.SetCompleted(task, json);

            Assert.Equal(CliTaskStatus.Completed, task.Status);
            Assert.Equal(json, task.ResultJson);
            Assert.NotNull(task.CompletedAt);
            Assert.True(task.Tcs.Task.IsCompleted);
            Assert.Equal(json, task.Tcs.Task.Result);
        }

        [Fact]
        public void SetCompleted_BroadcastsCompletedEvent()
        {
            var task = NewTask();
            string? seenEvent = null;
            task.OnSseEvent += (name, json) => seenEvent = name;

            TaskStateMachine.SetCompleted(task, "{}");

            Assert.Equal("completed", seenEvent);
        }

        [Fact]
        public void SetCompleted_NoOpOnNullTask()
        {
            TaskStateMachine.SetCompleted(null, "{}");
        }

        [Fact]
        public void SetFailed_TransitionsToFailedAndCompletesTcs()
        {
            var task = NewTask();
            var errorJson = "{\"error\":\"boom\"}";

            TaskStateMachine.SetFailed(task, errorJson);

            Assert.Equal(CliTaskStatus.Failed, task.Status);
            Assert.Equal(errorJson, task.ResultJson);
            Assert.NotNull(task.CompletedAt);
            Assert.True(task.Tcs.Task.IsCompleted);
            Assert.Equal(errorJson, task.Tcs.Task.Result);
        }

        [Fact]
        public void SetFailed_BroadcastsFailedEvent()
        {
            var task = NewTask();
            string? seenEvent = null;
            task.OnSseEvent += (name, json) => seenEvent = name;

            TaskStateMachine.SetFailed(task, "{}");

            Assert.Equal("failed", seenEvent);
        }

        [Fact]
        public void SetFailed_NoOpOnNullTask()
        {
            TaskStateMachine.SetFailed(null, "{}");
        }

        [Fact]
        public async Task DoubleCompletion_DoesNotThrow()
        {
            // TrySetResult should guard against InvalidOperationException.
            var task = NewTask();

            TaskStateMachine.SetCompleted(task, "{\"first\":true}");
            // Second completion is a no-op on the TCS but still updates fields.
            TaskStateMachine.SetCompleted(task, "{\"second\":true}");

            // The first result wins (TrySetResult returns false on second call).
            Assert.Equal("{\"second\":true}", task.ResultJson);
            Assert.Equal("{\"first\":true}", await task.Tcs.Task);
        }

        [Fact]
        public void SafeParseJson_FallsBackToRawStringOnInvalidJson()
        {
            // SetCompleted broadcasts a "completed" event whose result is
            // SafeParseJson(resultJson). If resultJson is not valid JSON,
            // the broadcast should still succeed (falling back to raw string),
            // not throw.
            var task = NewTask();
            // Invalid JSON should not break the broadcast.
            TaskStateMachine.SetCompleted(task, "not json at all");

            Assert.Equal(CliTaskStatus.Completed, task.Status);
        }
    }
}
