using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace RevitCliBridge
{
    /// <summary>
    /// Lightweight in-memory metrics collector for local observability. Uses
    /// atomic counters (lock-free) for global aggregates and a ConcurrentDictionary
    /// for per-command breakdowns. Not a prometheus exporter — just a snapshot
    /// for the /api/metrics endpoint.
    /// </summary>
    public static class MetricsCollector
    {
        private static readonly DateTime _startedAt = DateTime.UtcNow;
        private static int _totalCommands;
        private static int _totalErrors;
        private static long _totalDurationMs;
        private static int _activeTasks;
        private static readonly ConcurrentDictionary<string, CommandStats> _byCommand = new();

        /// <summary>
        /// Records a single command execution. Call after the command completes
        /// (success or failure).
        /// </summary>
        public static void RecordCommand(string command, long durationMs, bool success)
        {
            Interlocked.Increment(ref _totalCommands);
            Interlocked.Add(ref _totalDurationMs, durationMs);
            if (!success)
                Interlocked.Increment(ref _totalErrors);

            _byCommand.AddOrUpdate(
                command,
                // Factory: first sighting of this command
                _ => new CommandStats
                {
                    Count = 1,
                    Errors = success ? 0 : 1,
                    TotalDurationMs = durationMs
                },
                // Update: accumulate into existing stats
                (_, existing) => new CommandStats
                {
                    Count = existing.Count + 1,
                    Errors = existing.Errors + (success ? 0 : 1),
                    TotalDurationMs = existing.TotalDurationMs + durationMs
                });
        }

        public static void IncrementActiveTasks() => Interlocked.Increment(ref _activeTasks);
        public static void DecrementActiveTasks() => Interlocked.Decrement(ref _activeTasks);

        /// <summary>
        /// Returns a snapshot suitable for JSON serialization. Reads are
        /// eventually-consistent (atomic reads of each counter).
        /// </summary>
        public static object GetSnapshot()
        {
            int total = Interlocked.CompareExchange(ref _totalCommands, 0, 0);
            int errors = Interlocked.CompareExchange(ref _totalErrors, 0, 0);
            long totalMs = Interlocked.CompareExchange(ref _totalDurationMs, 0, 0);
            int active = Interlocked.CompareExchange(ref _activeTasks, 0, 0);

            var byCommand = new Dictionary<string, object>();
            foreach (var kvp in _byCommand)
            {
                var s = kvp.Value;
                byCommand[kvp.Key] = new
                {
                    count = s.Count,
                    errors = s.Errors,
                    avg_duration_ms = s.Count > 0 ? s.TotalDurationMs / s.Count : 0
                };
            }

            return new
            {
                uptime_seconds = (int)(DateTime.UtcNow - _startedAt).TotalSeconds,
                total_commands = total,
                total_errors = errors,
                avg_duration_ms = total > 0 ? (int)(totalMs / total) : 0,
                active_tasks = active,
                by_command = byCommand
            };
        }

        /// <summary>
        /// Resets all counters. Primarily for tests; not exposed via API.
        /// </summary>
        public static void Reset()
        {
            Interlocked.Exchange(ref _totalCommands, 0);
            Interlocked.Exchange(ref _totalErrors, 0);
            Interlocked.Exchange(ref _totalDurationMs, 0);
            Interlocked.Exchange(ref _activeTasks, 0);
            _byCommand.Clear();
        }

        private class CommandStats
        {
            public int Count;
            public int Errors;
            public long TotalDurationMs;
        }
    }
}
