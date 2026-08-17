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

        // ---------- Edge cases: state transition ordering ----------

        [Fact]
        public void SetRunning_Twice_SecondCallIsIdempotentForStatus()
        {
            // No exception is thrown and the task remains Running. This
            // mirrors the production guarantee that re-entering the executor
            // loop does not corrupt task state.
            var task = NewTask();
            TaskStateMachine.SetRunning(task);

            TaskStateMachine.SetRunning(task);

            Assert.Equal(CliTaskStatus.Running, task.Status);
            // Re-stamping StartedAt is allowed by the state machine — the
            // contract is just "must be non-null after SetRunning".
            Assert.NotNull(task.StartedAt);
        }

        [Fact]
        public void SetProgress_BeforeSetRunning_StillUpdatesProgress()
        {
            // No state precondition — SetProgress updates fields regardless
            // of prior status. Production code normally calls SetRunning
            // first, but the state machine doesn't enforce the ordering.
            var task = NewTask();

            TaskStateMachine.SetProgress(task, 25, "early");

            Assert.Equal(25, task.Progress);
            Assert.Equal("early", task.ProgressMessage);
            Assert.Equal(CliTaskStatus.Pending, task.Status);
        }

        [Fact]
        public void SetProgress_NegativeProgress_IsStoredWithoutClamping()
        {
            // No clamping in the state machine — contract is "store what
            // caller gives". Clamping, if ever added, must be intentional
            // and would update this test.
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
            // A task that was marked Failed may be re-marked Completed by
            // a recovery path. The state machine allows this; the TCS
            // already has the failed result, so TrySetResult returns false
            // and the Failed result remains as the awaitable value.
            var task = NewTask();
            TaskStateMachine.SetFailed(task, "{\"err\":true}");

            TaskStateMachine.SetCompleted(task, "{\"ok\":true}");

            Assert.Equal(CliTaskStatus.Completed, task.Status);
            Assert.Equal("{\"ok\":true}", task.ResultJson);
        }

        [Fact]
        public async Task SetCompleted_AfterSetFailed_TcsKeepsFirstResult()
        {
            // TrySetResult is one-shot: the first completion wins.
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
            // SafeParseJson should produce a JObject in the broadcast payload
            // when the input is valid JSON. We can't inspect the parsed
            // object directly (it's an anonymous-typed payload), but the
            // serialized broadcast JSON should contain the nested fields
            // rather than a quoted string.
            var task = NewTask();
            string? seenJson = null;
            task.OnSseEvent += (_, json) => seenJson = json;

            TaskStateMachine.SetCompleted(task, "{\"count\":3,\"name\":\"wall\"}");

            Assert.NotNull(seenJson);
            // The broadcast payload nests the parsed object under "result".
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
            // The state machine passes the (possibly null) message straight
            // to the broadcast payload, and the ProgressMessage field on
            // the task is NOT overwritten when null is passed (the
            // `if (message is not null)` guard skips the assignment).
            var task = NewTask();
            TaskStateMachine.SetRunning(task);
            string? seenJson = null;
            task.OnSseEvent += (_, json) => seenJson = json;

            TaskStateMachine.SetProgress(task, 50, null);

            Assert.Contains("\"progress\":50", seenJson);
            // SetRunning broadcasts "Execution started" inline but does NOT
            // update task.ProgressMessage, so it stays null. SetProgress with
            // a null message must preserve this null (not overwrite).
            Assert.Null(task.ProgressMessage);
        }
    }
}
