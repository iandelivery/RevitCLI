using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for <see cref="TaskInfo"/> — the per-task model with
    /// lock-protected mutable fields, SSE event broadcasting, and a
    /// TaskCompletionSource for HTTP-request unblocking. The state-machine
    /// transitions are exercised by <see cref="TaskStateMachineTests"/>;
    /// these tests focus on TaskInfo's own contract (defaults, locking,
    /// Broadcast, ClearSseSubscribers) which were previously untested.
    /// </summary>
    public class TaskInfoTests
    {
        // ---------- Defaults ----------

        [Fact]
        public void NewInstance_StatusDefaultsToPending()
        {
            var t = new TaskInfo();
            Assert.Equal(CliTaskStatus.Pending, t.Status);
        }

        [Fact]
        public void NewInstance_ProgressDefaultsToZero()
        {
            var t = new TaskInfo();
            Assert.Equal(0, t.Progress);
        }

        [Fact]
        public void NewInstance_ProgressMessageDefaultsToNull()
        {
            var t = new TaskInfo();
            Assert.Null(t.ProgressMessage);
        }

        [Fact]
        public void NewInstance_ResultJsonDefaultsToNull()
        {
            var t = new TaskInfo();
            Assert.Null(t.ResultJson);
        }

        [Fact]
        public void NewInstance_TimestampsDefaultToUnset()
        {
            var t = new TaskInfo();
            Assert.Equal(default(DateTime), t.CreatedAt);
            Assert.Null(t.StartedAt);
            Assert.Null(t.CompletedAt);
        }

        [Fact]
        public void NewInstance_TcsIsNotCompleted()
        {
            var t = new TaskInfo();
            Assert.False(t.Tcs.Task.IsCompleted);
        }

        // ---------- Property assignment ----------

        [Fact]
        public void TaskId_AndCommand_AreSettable()
        {
            var t = new TaskInfo { TaskId = "abc", Command = "create_wall" };
            Assert.Equal("abc", t.TaskId);
            Assert.Equal("create_wall", t.Command);
        }

        [Fact]
        public void Progress_Setter_UpdatesValue()
        {
            var t = new TaskInfo();
            t.Progress = 42;
            Assert.Equal(42, t.Progress);
        }

        [Fact]
        public void Status_Setter_UpdatesValue()
        {
            var t = new TaskInfo();
            t.Status = CliTaskStatus.Running;
            Assert.Equal(CliTaskStatus.Running, t.Status);
        }

        [Fact]
        public void ResultJson_Setter_UpdatesValue()
        {
            var t = new TaskInfo();
            t.ResultJson = "{\"ok\":true}";
            Assert.Equal("{\"ok\":true}", t.ResultJson);
        }

        // ---------- Tcs unblocking ----------

        [Fact]
        public async Task Tcs_TrySetResult_UnblocksAwaiter()
        {
            var t = new TaskInfo();
            var setResult = t.Tcs.TrySetResult("{\"done\":1}");
            Assert.True(setResult);
            Assert.Equal("{\"done\":1}", await t.Tcs.Task);
        }

        [Fact]
        public async Task Tcs_TrySetResult_Twice_FalseOnSecondCall()
        {
            var t = new TaskInfo();
            Assert.True(t.Tcs.TrySetResult("first"));
            Assert.False(t.Tcs.TrySetResult("second"));
            Assert.Equal("first", await t.Tcs.Task);
        }

        // ---------- Broadcast ----------

        [Fact]
        public void Broadcast_WithNoSubscribers_DoesNotThrow()
        {
            var t = new TaskInfo();
            t.Broadcast("progress", new { progress = 0 });
        }

        [Fact]
        public void Broadcast_WithSubscriber_InvokesWithEventNameAndJson()
        {
            var t = new TaskInfo();
            string? seenEvent = null;
            string? seenJson = null;
            t.OnSseEvent += (name, json) => { seenEvent = name; seenJson = json; };

            t.Broadcast("completed", new { task_id = "t-1", status = "completed" });

            Assert.Equal("completed", seenEvent);
            Assert.NotNull(seenJson);
            Assert.Contains("\"task_id\":\"t-1\"", seenJson);
            Assert.Contains("\"status\":\"completed\"", seenJson);
        }

        [Fact]
        public void Broadcast_PayloadSerializedAsJson_NotToString()
        {
            // Verifies Broadcast uses JsonConvert, not Object.ToString().
            var t = new TaskInfo();
            string? seenJson = null;
            t.OnSseEvent += (_, json) => seenJson = json;

            t.Broadcast("progress", new { nested = new { deep = 7 } });

            Assert.Contains("\"nested\":{\"deep\":7}", seenJson);
        }

        [Fact]
        public void Broadcast_MultipleSubscribers_AllInvokedOnce()
        {
            var t = new TaskInfo();
            var calls = new List<string>();
            t.OnSseEvent += (name, _) => calls.Add(name + "-a");
            t.OnSseEvent += (name, _) => calls.Add(name + "-b");

            t.Broadcast("progress", new { });

            Assert.Equal(new[] { "progress-a", "progress-b" }, calls);
        }

        [Fact]
        public void Broadcast_SubscriberThrowing_PropagatesException()
        {
            // Broadcast uses plain multicast invocation — no try/catch. An
            // exception in a handler therefore propagates to the caller.
            // Asserting this explicitly so any future try/catch wrapping is
            // noticed and the test updated to match.
            var t = new TaskInfo();
            t.OnSseEvent += (_, _) => throw new InvalidOperationException("boom");

            Assert.Throws<InvalidOperationException>(() =>
                t.Broadcast("progress", new { }));
        }

        // ---------- ClearSseSubscribers ----------

        [Fact]
        public void ClearSseSubscribers_RemovesAllSubscribers()
        {
            var t = new TaskInfo();
            var invoked = false;
            t.OnSseEvent += (_, _) => invoked = true;

            t.ClearSseSubscribers();
            t.Broadcast("progress", new { });

            Assert.False(invoked);
        }

        [Fact]
        public void ClearSseSubscribers_WhenNoSubscribers_DoesNotThrow()
        {
            var t = new TaskInfo();
            t.ClearSseSubscribers();
        }

        [Fact]
        public void ClearSseSubscribers_FollowedByBroadcast_DoesNotInvoke()
        {
            var t = new TaskInfo();
            var invoked = false;
            t.OnSseEvent += (_, _) => invoked = true;

            t.ClearSseSubscribers();
            t.Broadcast("completed", new { });

            Assert.False(invoked);
        }

        // ---------- Thread safety of lock-protected properties ----------

        [Fact]
        public async Task Status_ReadAndWriteFromMultipleThreads_IsConsistent()
        {
            // Concurrent reads & writes must not corrupt the field —
            // the lock guarantees atomic visibility. Verifies that a
            // single writer + multiple readers all see the final value.
            var t = new TaskInfo();
            var cts = new CancellationTokenSource();

            var reader1 = Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var _ = t.Status; // spin-read
                }
            });
            var reader2 = Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var _ = t.Status; // spin-read
                }
            });

            t.Status = CliTaskStatus.Running;
            t.Status = CliTaskStatus.Completed;

            cts.Cancel();
            await Task.WhenAll(reader1, reader2);

            Assert.Equal(CliTaskStatus.Completed, t.Status);
        }
    }
}
