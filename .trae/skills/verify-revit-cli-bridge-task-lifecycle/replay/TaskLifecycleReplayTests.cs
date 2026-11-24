using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using RevitCliBridge;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Replay of the TaskLifecycle.tla bug paths against the real
    /// <see cref="TaskStateMachine"/>: two threads end the same <see cref="TaskInfo"/>
    /// at once and the task record, the TCS result and the SSE frames must still
    /// describe one outcome. Copied into the test project by run.ps1, never committed there.
    /// </summary>
    public class TaskLifecycleReplayTests
    {
        private const string OkJson = "{\"status\":\"success\",\"data\":{\"element_id\":12345}}";
        private const string TimeoutJson = "{\"status\":\"error\",\"message\":\"Revit API execution timed out.\"}";
        private const int Trials = 20000;

        private static TaskInfo NewTask(int id, List<string>? wire)
        {
            var task = new TaskInfo { TaskId = $"replay-{id}", Command = "create_wall" };
            if (wire != null)
            {
                task.OnSseEvent += (name, _) => { lock (wire) wire.Add(name); };
            }
            return task;
        }

        private static string? OutcomeOf(string? json)
        {
            if (json == null) return null;
            if (json == OkJson) return "completed";
            if (json == TimeoutJson) return "timeout";
            return "other";
        }

        private static List<string> TerminalFrames(List<string> wire)
        {
            var frames = new List<string>();
            foreach (var name in wire)
            {
                if (name == "completed" || name == "failed") frames.Add(name);
            }
            return frames;
        }

        private static string? TcsJson(TaskInfo task)
            => task.Tcs.Task.IsCompleted ? task.Tcs.Task.Result : null;

        private static string Describe(TaskInfo task, List<string> wire)
        {
            return $"status={task.Status} result={OutcomeOf(task.ResultJson) ?? "none"} tcs={OutcomeOf(TcsJson(task)) ?? "none"} wire=[{string.Join(",", wire)}]";
        }

        private static bool Consistent(TaskInfo task, List<string> wire)
        {
            string? tcs = TcsJson(task);
            string? statusOutcome = task.Status switch
            {
                CliTaskStatus.Completed => "completed",
                CliTaskStatus.Timeout => "timeout",
                _ => "other",
            };
            if (statusOutcome == "other") return false;
            if (OutcomeOf(task.ResultJson) != statusOutcome) return false;
            if (OutcomeOf(tcs) != statusOutcome) return false;
            var terminal = TerminalFrames(wire);
            if (terminal.Count == 0) return false;
            foreach (var frame in terminal)
            {
                if ((frame == "completed") != (statusOutcome == "completed")) return false;
            }
            int firstTerminal = wire.FindIndex(f => f == "completed" || f == "failed");
            for (int i = firstTerminal + 1; i < wire.Count; i++)
            {
                if (wire[i] != wire[firstTerminal]) return false;
            }
            return true;
        }

        private static (int torn, long closestNs, List<string> samples) Race(
            Action<TaskInfo> a,
            Action<TaskInfo> b,
            Action<TaskInfo>? after = null)
        {
            var tasks = new TaskInfo[Trials];
            var wires = new List<string>[Trials];
            for (int i = 0; i < Trials; i++)
            {
                wires[i] = new List<string>();
                tasks[i] = NewTask(i, wires[i]);
            }

            long[] gates = new long[Trials];
            long[] entryA = new long[Trials];
            long[] entryB = new long[Trials];
            long start = Stopwatch.GetTimestamp();
            long interval = Math.Max(1, (long)(Stopwatch.Frequency * 30e-6));
            for (int i = 0; i < Trials; i++) gates[i] = start + interval * (i + 50);

            using var ready = new CountdownEvent(2);
            using var done = new CountdownEvent(2);
            var threadA = new Thread(() =>
            {
                ready.Signal();
                for (int i = 0; i < Trials; i++)
                {
                    while (Stopwatch.GetTimestamp() < gates[i]) Thread.SpinWait(8);
                    entryA[i] = Stopwatch.GetTimestamp();
                    a(tasks[i]);
                }
                done.Signal();
            }) { Priority = ThreadPriority.Highest, IsBackground = true };
            var threadB = new Thread(() =>
            {
                ready.Signal();
                for (int i = 0; i < Trials; i++)
                {
                    while (Stopwatch.GetTimestamp() < gates[i]) Thread.SpinWait(8);
                    entryB[i] = Stopwatch.GetTimestamp();
                    b(tasks[i]);
                }
                done.Signal();
            }) { Priority = ThreadPriority.Highest, IsBackground = true };
            threadA.Start();
            threadB.Start();
            ready.Wait();
            done.Wait(TimeSpan.FromSeconds(60));

            long best = long.MaxValue;
            int torn = 0;
            var samples = new List<string>();
            for (int i = 0; i < Trials; i++)
            {
                if (entryA[i] > 0 && entryB[i] > 0)
                {
                    long spread = Math.Abs(entryA[i] - entryB[i]);
                    if (spread < best) best = spread;
                }
                after?.Invoke(tasks[i]);
                if (!Consistent(tasks[i], wires[i]))
                {
                    torn++;
                    if (samples.Count < 5) samples.Add(Describe(tasks[i], wires[i]));
                }
            }
            return (torn, best == long.MaxValue ? -1 : best * 1_000_000_000 / Stopwatch.Frequency, samples);
        }

        [Fact]
        public void TestVerify_Sequential_FirstTerminalWins()
        {
            var wire1 = new List<string>();
            var task1 = NewTask(1, wire1);
            TaskStateMachine.SetCompleted(task1, OkJson);
            TaskStateMachine.SetTimeout(task1, TimeoutJson);
            TaskStateMachine.SetFailed(task1, TimeoutJson);
            Assert.Equal(CliTaskStatus.Completed, task1.Status);
            Assert.Equal(OkJson, task1.ResultJson);
            Assert.Equal("completed", OutcomeOf(TcsJson(task1)));
            Assert.Equal(new[] { "completed" }, wire1);

            var wire2 = new List<string>();
            var task2 = NewTask(2, wire2);
            TaskStateMachine.SetTimeout(task2, TimeoutJson);
            TaskStateMachine.SetCompleted(task2, OkJson);
            TaskStateMachine.SetRunning(task2);
            Assert.Equal(CliTaskStatus.Timeout, task2.Status);
            Assert.Equal(TimeoutJson, task2.ResultJson);
            Assert.Equal("timeout", OutcomeOf(TcsJson(task2)));
            Assert.Equal(new[] { "failed" }, wire2);
            Assert.Null(task2.StartedAt);
        }

        [Fact]
        public void TestVerify_Concurrent_CompletionVsTimeout_KeepFirstTerminal()
        {
            var (torn, closestNs, samples) = Race(
                t => TaskStateMachine.SetCompleted(t, OkJson),
                t => TaskStateMachine.SetTimeout(t, TimeoutJson));
            Assert.True(closestNs >= 0 && closestNs < 2000,
                $"INCONCLUSIVE: the two calls never ran close together (closest approach {closestNs} ns over {Trials} trials); rerun on a machine with two or more idle cores");
            Assert.True(torn == 0,
                $"{torn} of {Trials} trials ended with a task record, a TCS result or SSE frames that disagree (closest approach {closestNs} ns): {string.Join(" | ", samples)}");
        }

        [Fact]
        public void TestVerify_Concurrent_StartVsTimeout_TimedOutTaskStaysEnded()
        {
            var (torn, closestNs, samples) = Race(
                t => TaskStateMachine.SetRunning(t),
                t => TaskStateMachine.SetTimeout(t, TimeoutJson),
                after: t => TaskStateMachine.SetCompleted(t, OkJson));
            Assert.True(closestNs >= 0 && closestNs < 2000,
                $"INCONCLUSIVE: the two calls never ran close together (closest approach {closestNs} ns over {Trials} trials); rerun on a machine with two or more idle cores");
            Assert.True(torn == 0,
                $"{torn} of {Trials} trials ended with a task record, a TCS result or SSE frames that disagree (closest approach {closestNs} ns): {string.Join(" | ", samples)}");
        }
    }
}
