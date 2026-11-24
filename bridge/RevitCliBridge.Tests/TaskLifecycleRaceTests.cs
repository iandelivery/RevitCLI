using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using RevitCliBridge;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Regression tests for the task lifecycle race: two threads end the same
    /// <see cref="TaskInfo"/> at once (the Revit thread and the HTTP thread's
    /// timeout) and the record, the TCS result, and the SSE frames must still
    /// describe one outcome. The harness releases both calls on the same clock
    /// tick, which is the only way a unit test reaches the window. Mirrors the
    /// replay in .trae/skills/verify-revit-cli-bridge-task-lifecycle.
    /// </summary>
    public class TaskLifecycleRaceTests
    {
        private const string OkJson = "{\"status\":\"success\",\"data\":{\"element_id\":12345}}";
        private const string TimeoutJson = "{\"status\":\"error\",\"message\":\"Revit API execution timed out.\"}";
        private const int Trials = 20000;

        private static TaskInfo NewTask(int id, List<string> wire)
        {
            var task = new TaskInfo { TaskId = $"race-{id}", Command = "create_wall" };
            task.OnSseEvent += (name, _) => { lock (wire) wire.Add(name); };
            return task;
        }

        private static string? OutcomeOf(string? json)
        {
            if (json == null) return null;
            if (json == OkJson) return "completed";
            if (json == TimeoutJson) return "timeout";
            return "other";
        }

        private static string? TcsJson(TaskInfo task)
            => task.Tcs.Task.IsCompleted ? task.Tcs.Task.Result : null;

        private static List<string> TerminalFrames(List<string> wire)
        {
            var frames = new List<string>();
            foreach (var name in wire)
            {
                if (name == "completed" || name == "failed") frames.Add(name);
            }
            return frames;
        }

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

            using var done = new CountdownEvent(2);
            var threadA = new Thread(() =>
            {
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
        public void SetCompleted_And_SetTimeout_FromTwoThreads_KeepOneOutcome()
        {
            var (torn, closestNs, samples) = Race(
                t => TaskStateMachine.SetCompleted(t, OkJson),
                t => TaskStateMachine.SetTimeout(t, TimeoutJson));
            Assert.True(torn == 0,
                $"{torn} of {Trials} trials ended with a task record, a TCS result or SSE frames that disagree (closest approach {closestNs} ns): {string.Join(" | ", samples)}");
        }

        [Fact]
        public void SetRunning_And_SetTimeout_FromTwoThreads_TimedOutTaskStaysEnded()
        {
            var (torn, closestNs, samples) = Race(
                t => TaskStateMachine.SetRunning(t),
                t => TaskStateMachine.SetTimeout(t, TimeoutJson),
                after: t => TaskStateMachine.SetCompleted(t, OkJson));
            Assert.True(torn == 0,
                $"{torn} of {Trials} trials ended with a task record, a TCS result or SSE frames that disagree (closest approach {closestNs} ns): {string.Join(" | ", samples)}");
        }

        [Fact]
        public void SetRunning_AfterSetTimeout_ReturnsFalseAndKeepsTimeout()
        {
            var wire = new List<string>();
            var task = NewTask(0, wire);

            TaskStateMachine.SetTimeout(task, TimeoutJson);
            bool started = TaskStateMachine.SetRunning(task);

            Assert.False(started);
            Assert.Equal(CliTaskStatus.Timeout, task.Status);
            Assert.Null(task.StartedAt);
            Assert.Equal(new[] { "failed" }, wire);
        }
    }
}
