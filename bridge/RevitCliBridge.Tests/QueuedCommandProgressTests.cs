using System;
using Newtonsoft.Json;
using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for the progress-reporting channel on <see cref="QueuedCommand"/>.
    /// The bridge wires <see cref="QueuedCommand.ProgressReporter"/> just
    /// before dispatch; these tests cover the envelope-side contract
    /// (no-op when unwired, forwarding, JSON exclusion). The router wiring
    /// itself lives in the bridge project and requires the Revit API, so it
    /// is verified by compilation and manual smoke tests instead.
    /// </summary>
    public class QueuedCommandProgressTests
    {
        [Fact]
        public void ReportProgress_NoReporterWired_IsNoOp()
        {
            var cmd = new QueuedCommand();

            // Must not throw even though no bridge wired a reporter
            // (unit tests, standalone execution).
            cmd.ReportProgress(50, "halfway");
        }

        [Fact]
        public void ReportProgress_ForwardsPercentAndMessage()
        {
            int? seenPercent = null;
            string? seenMessage = null;
            var cmd = new QueuedCommand
            {
                ProgressReporter = (p, m) => { seenPercent = p; seenMessage = m; }
            };

            cmd.ReportProgress(75, "almost done");

            Assert.Equal(75, seenPercent);
            Assert.Equal("almost done", seenMessage);
        }

        [Fact]
        public void ReportProgress_OmittedMessage_ForwardedAsNull()
        {
            string? seenMessage = "sentinel";
            var cmd = new QueuedCommand
            {
                ProgressReporter = (p, m) => seenMessage = m
            };

            cmd.ReportProgress(10);

            Assert.Null(seenMessage);
        }

        [Fact]
        public void ReportProgress_MultipleCalls_EachForwards()
        {
            var calls = new System.Collections.Generic.List<(int, string?)>();
            var cmd = new QueuedCommand
            {
                ProgressReporter = (p, m) => calls.Add((p, m))
            };

            cmd.ReportProgress(25, "quarter");
            cmd.ReportProgress(50, "half");
            cmd.ReportProgress(100);

            Assert.Equal(3, calls.Count);
            Assert.Equal((25, "quarter"), calls[0]);
            Assert.Equal((50, "half"), calls[1]);
            Assert.Equal((100, (string?)null), calls[2]);
        }

        [Fact]
        public void ProgressReporter_ExcludedFromJsonSerialization()
        {
            var cmd = new QueuedCommand
            {
                TaskId = "task-1",
                Command = "create_wall",
                ProgressReporter = (p, m) => { }
            };

            var json = JsonConvert.SerializeObject(cmd);

            // The delegate must never leak into request/response payloads.
            Assert.DoesNotContain("ProgressReporter", json);
            Assert.Contains("\"task_id\":\"task-1\"", json);
        }
    }
}
