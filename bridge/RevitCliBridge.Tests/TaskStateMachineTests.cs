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
        public async Task SetCompleted_TransitionsAndStampsAndCompletesTcs()
        {
            var task = NewTask();
            var json = "{\"status\":\"ok\"}";

            TaskStateMachine.SetCompleted(task, json);

            Assert.Equal(CliTaskStatus.Completed, task.Status);
            Assert.Equal(json, task.ResultJson);
            Assert.NotNull(task.CompletedAt);
            Assert.True(task.Tcs.Task.IsCompleted);
            Assert.Equal(json, await task.Tcs.Task);
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
        public async Task SetFailed_TransitionsToFailedAndCompletesTcs()
        {
            var task = NewTask();
            var errorJson = "{\"error\":\"boom\"}";

            TaskStateMachine.SetFailed(task, errorJson);

            Assert.Equal(CliTaskStatus.Failed, task.Status);
            Assert.Equal(errorJson, task.ResultJson);
            Assert.NotNull(task.CompletedAt);
            Assert.True(task.Tcs.Task.IsCompleted);
            Assert.Equal(errorJson, await task.Tcs.Task);
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
            var task = NewTask();

            TaskStateMachine.SetCompleted(task, "{\"first\":true}");
            TaskStateMachine.SetCompleted(task, "{\"second\":true}");

            Assert.Equal("{\"second\":true}", task.ResultJson);
            Assert.Equal("{\"first\":true}", await task.Tcs.Task);
        }

        [Fact]
        public void SafeParseJson_FallsBackToRawStringOnInvalidJson()
        {
            var task = NewTask();
            TaskStateMachine.SetCompleted(task, "not json at all");

            Assert.Equal(CliTaskStatus.Completed, task.Status);
        }

        [Fact]
        public void SetRunning_Twice_SecondCallIsIdempotentForStatus()
        {
            var task = NewTask();
            TaskStateMachine.SetRunning(task);

            TaskStateMachine.SetRunning(task);

            Assert.Equal(CliTaskStatus.Running, task.Status);
            Assert.NotNull(task.StartedAt);
        }

        [Fact]
        public void SetProgress_BeforeSetRunning_StillUpdatesProgress()
        {
            var task = NewTask();

            TaskStateMachine.SetProgress(task, 25, "early");

            Assert.Equal(25, task.Progress);
            Assert.Equal("early", task.ProgressMessage);
            Assert.Equal(CliTaskStatus.Pending, task.Status);
        }

        [Fact]
        public void SetProgress_NegativeProgress_IsStoredWithoutClamping()
        {
            var task = NewTask();
            TaskStateMachine.SetRunning(task);

            TaskStateMachine.SetProgress(task, -5, "oops");

            Assert.Equal(-5, task.Progress);
        }

        [Fact]
        public void SetProgress_ProgressAbove100_IsStoredWithoutClamping()
        {
            var task = NewTask();
            TaskStateMachine.SetRunning(task);

            TaskStateMachine.SetProgress(task, 150, "overflow");

            Assert.Equal(150, task.Progress);
        }

        [Fact]
        public void SetCompleted_OverwritesPreviousFailedState()
        {
            var task = NewTask();
            TaskStateMachine.SetFailed(task, "{\"err\":true}");

            TaskStateMachine.SetCompleted(task, "{\"ok\":true}");

            Assert.Equal(CliTaskStatus.Completed, task.Status);
            Assert.Equal("{\"ok\":true}", task.ResultJson);
        }

        [Fact]
        public async Task SetCompleted_AfterSetFailed_TcsKeepsFirstResult()
        {
            var task = NewTask();
            TaskStateMachine.SetFailed(task, "first");

            TaskStateMachine.SetCompleted(task, "second");

            Assert.Equal("first", await task.Tcs.Task);
        }

        [Fact]
        public async Task SetFailed_AfterSetCompleted_TcsKeepsFirstResult()
        {
            var task = NewTask();
            TaskStateMachine.SetCompleted(task, "first-ok");

            TaskStateMachine.SetFailed(task, "later-fail");

            Assert.Equal("first-ok", await task.Tcs.Task);
            Assert.Equal(CliTaskStatus.Failed, task.Status);
        }

        [Fact]
        public void Lifecycle_TimestampOrdering_StartedBeforeCompleted()
        {
            var task = NewTask();
            TaskStateMachine.SetRunning(task);
            TaskStateMachine.SetCompleted(task, "{}");

            Assert.NotNull(task.StartedAt);
            Assert.NotNull(task.CompletedAt);
            Assert.True(task.StartedAt <= task.CompletedAt);
        }

        [Fact]
        public void SetCompleted_BroadcastsParsedJson_WhenInputIsValidJson()
        {
            var task = NewTask();
            string? seenJson = null;
            task.OnSseEvent += (_, json) => seenJson = json;

            TaskStateMachine.SetCompleted(task, "{\"count\":3,\"name\":\"wall\"}");

            Assert.NotNull(seenJson);
            Assert.Contains("\"result\":", seenJson!);
            Assert.Contains("\"count\":3", seenJson!);
            Assert.Contains("\"name\":\"wall\"", seenJson!);
        }

        [Fact]
        public void SetCompleted_BroadcastContainsTaskIdAndStatus()
        {
            var task = NewTask(id: "task-99");
            string? seenJson = null;
            task.OnSseEvent += (_, json) => seenJson = json;

            TaskStateMachine.SetCompleted(task, "{}");

            Assert.Contains("\"task_id\":\"task-99\"", seenJson);
            Assert.Contains("\"status\":\"completed\"", seenJson);
        }

        [Fact]
        public void SetFailed_BroadcastContainsTaskIdAndFailedStatus()
        {
            var task = NewTask(id: "task-fail");
            string? seenJson = null;
            task.OnSseEvent += (_, json) => seenJson = json;

            TaskStateMachine.SetFailed(task, "{\"error\":\"boom\"}");

            Assert.Contains("\"task_id\":\"task-fail\"", seenJson);
            Assert.Contains("\"status\":\"failed\"", seenJson);
        }

        [Fact]
        public void SetRunning_BroadcastProgressZero_ContainsTaskId()
        {
            var task = NewTask(id: "task-run");
            string? seenJson = null;
            task.OnSseEvent += (name, json) =>
            {
                if (name == "progress") seenJson = json;
            };

            TaskStateMachine.SetRunning(task);

            Assert.Contains("\"task_id\":\"task-run\"", seenJson);
            Assert.Contains("\"progress\":0", seenJson);
        }

        [Fact]
        public void SetProgress_BroadcastIncludesCurrentMessage_WhenProvided()
        {
            var task = NewTask();
            TaskStateMachine.SetRunning(task);
            string? seenJson = null;
            task.OnSseEvent += (_, json) => seenJson = json;

            TaskStateMachine.SetProgress(task, 50, "halfway there");

            Assert.Contains("\"progress\":50", seenJson);
            Assert.Contains("\"message\":\"halfway there\"", seenJson);
        }

        [Fact]
        public void SetProgress_BroadcastMessageIsNull_WhenNullPassed()
        {
            var task = NewTask();
            TaskStateMachine.SetRunning(task);
            string? seenJson = null;
            task.OnSseEvent += (_, json) => seenJson = json;

            TaskStateMachine.SetProgress(task, 50, null);

            Assert.Contains("\"progress\":50", seenJson);
            Assert.Null(task.ProgressMessage);
        }
    }
}
