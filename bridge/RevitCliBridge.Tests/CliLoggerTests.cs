using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for <see cref="CliLogger"/> — the structured, daily-rotated,
    /// buffered file logger. CliLogger was previously untested despite being
    /// the bridge's sole observability surface; these tests cover log line
    /// formatting, structured field rendering, daily rollover, Shutdown
    /// flushing, and the LogDirectory path contract.
    ///
    /// Tests use reflection to redirect <c>_logDirectory</c> to a unique
    /// temp folder per test so the real LocalAppData log file is never
    /// touched and tests are deterministic &amp; independent. The class is in
    /// the <c>StaticStateSerial</c> collection so it never runs in parallel
    /// with another test class that calls CliLogger (e.g. PortAllocator).
    /// </summary>
    [Collection("StaticStateSerial")]
    public class CliLoggerTests : IDisposable
    {
        private static readonly FieldInfo LogDirectoryField =
            typeof(CliLogger).GetField("_logDirectory",
                BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly FieldInfo WriterField =
            typeof(CliLogger).GetField("_writer",
                BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly FieldInfo CurrentLogFileField =
            typeof(CliLogger).GetField("_currentLogFile",
                BindingFlags.NonPublic | BindingFlags.Static)!;

        private readonly string _tempDir;

        public CliLoggerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(),
                "revit-cli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            ResetCliLoggerState(_tempDir);
        }

        public void Dispose()
        {
            CliLogger.Shutdown();
            ResetCliLoggerState(null);
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private static void ResetCliLoggerState(string? newDir)
        {
            WriterField.SetValue(null, null);
            CurrentLogFileField.SetValue(null, null);
            LogDirectoryField.SetValue(null, newDir);
        }

        private static string TodayLogFile() =>
            Path.Combine(
                (string)LogDirectoryField.GetValue(null)!,
                $"cli_bridge_{DateTime.Now:yyyy-MM-dd}.log");

        // ---------- LogDirectory ----------

        [Fact]
        public void LogDirectory_ReturnsConfiguredPathAfterInjection()
        {
            Assert.Equal(_tempDir, CliLogger.LogDirectory);
            Assert.True(Directory.Exists(_tempDir));
        }

        [Fact]
        public void LogDirectory_IsStableAcrossCalls()
        {
            var first = CliLogger.LogDirectory;
            var second = CliLogger.LogDirectory;
            Assert.Equal(first, second);
        }

        // ---------- Plain message (no fields) ----------

        [Fact]
        public void Info_PlainMessage_WritesLineWithInfoLevel()
        {
            CliLogger.Info("hello world");
            CliLogger.Shutdown();

            var content = File.ReadAllText(TodayLogFile());
            Assert.Contains("[INFO] hello world", content);
        }

        [Fact]
        public void Warn_PlainMessage_WritesLineWithWarnLevel()
        {
            CliLogger.Warn("careful");
            CliLogger.Shutdown();

            var content = File.ReadAllText(TodayLogFile());
            Assert.Contains("[WARN] careful", content);
        }

        [Fact]
        public void Error_PlainMessage_WritesLineWithErrorLevel()
        {
            CliLogger.Error("kaboom");
            CliLogger.Shutdown();

            var content = File.ReadAllText(TodayLogFile());
            Assert.Contains("[ERROR] kaboom", content);
        }

        // ---------- Structured fields ----------

        [Fact]
        public void Info_StructuredFields_RendersKeyEqualsValuePairs()
        {
            CliLogger.Info("command_completed",
                ("request_id", "ab12cd34"),
                ("duration_ms", 42));
            CliLogger.Shutdown();

            var content = File.ReadAllText(TodayLogFile());
            Assert.Contains("[INFO] command_completed", content);
            Assert.Contains("request_id=ab12cd34", content);
            Assert.Contains("duration_ms=42", content);
        }

        [Fact]
        public void Info_QuotedField_QuotesStringsWithSpaces()
        {
            CliLogger.Info("plugin_rejected",
                ("reason", "unsigned and disallowed"));
            CliLogger.Shutdown();

            var content = File.ReadAllText(TodayLogFile());
            Assert.Contains("reason=\"unsigned and disallowed\"", content);
        }

        [Fact]
        public void Info_QuotedField_EscapesEmbeddedDoubleQuotes()
        {
            CliLogger.Info("parse_error",
                ("input", "bad \"value\""));
            CliLogger.Shutdown();

            var content = File.ReadAllText(TodayLogFile());
            Assert.Contains("input=\"bad \\\"value\\\"\"", content);
        }

        [Fact]
        public void Info_BareStringWithoutSpaces_NotQuoted()
        {
            CliLogger.Info("server_started",
                ("host", "localhost"));
            CliLogger.Shutdown();

            var content = File.ReadAllText(TodayLogFile());
            Assert.Contains("host=localhost", content);
            Assert.DoesNotContain("host=\"localhost\"", content);
        }

        [Fact]
        public void Info_BoolAndNumericFields_AreRenderedBare()
        {
            CliLogger.Info("dry_run",
                ("enabled", true),
                ("count", 0));
            CliLogger.Shutdown();

            var content = File.ReadAllText(TodayLogFile());
            Assert.Contains("enabled=True", content);
            Assert.Contains("count=0", content);
        }

        [Fact]
        public void Info_EmptyFieldsArray_RendersPlainMessage()
        {
            CliLogger.Info("no fields", Array.Empty<(string, object)>());
            CliLogger.Shutdown();

            var content = File.ReadAllText(TodayLogFile());
            var line = content.Split(new[] { Environment.NewLine },
                StringSplitOptions.RemoveEmptyEntries).Last();
            Assert.Contains("[INFO] no fields", line);
            // No trailing key=value pair should appear after the message.
            var suffix = line.Substring(line.IndexOf("no fields") + "no fields".Length);
            Assert.Equal(string.Empty, suffix.Trim());
        }

        // ---------- Buffering & flushing ----------

        [Fact]
        public void Shutdown_FlushesBufferedLinesToDisk()
        {
            // Before Shutdown, the 4KB buffer may not have flushed. After
            // Shutdown, every prior line must be readable from disk.
            CliLogger.Info("buffered line 1");
            CliLogger.Info("buffered line 2");
            CliLogger.Info("buffered line 3");
            CliLogger.Shutdown();

            var content = File.ReadAllText(TodayLogFile());
            Assert.Contains("buffered line 1", content);
            Assert.Contains("buffered line 2", content);
            Assert.Contains("buffered line 3", content);
        }

        [Fact]
        public void Shutdown_CalledMultipleTimes_DoesNotThrow()
        {
            CliLogger.Info("x");
            CliLogger.Shutdown();
            CliLogger.Shutdown();
            CliLogger.Shutdown();
        }

        [Fact]
        public void Log_AfterShutdown_ReopensFileAndAppends()
        {
            CliLogger.Info("before shutdown");
            CliLogger.Shutdown();

            CliLogger.Info("after shutdown");
            CliLogger.Shutdown();

            var content = File.ReadAllText(TodayLogFile());
            Assert.Contains("before shutdown", content);
            Assert.Contains("after shutdown", content);
        }

        // ---------- Timestamp format ----------

        [Fact]
        public void LogLine_PrefixedWithTimestampAndLevel()
        {
            CliLogger.Info("timestamped");
            CliLogger.Shutdown();

            var line = File.ReadAllLines(TodayLogFile())
                .First(l => l.Contains("timestamped"));
            // Format: [yyyy-MM-dd HH:mm:ss.fff] [INFO] timestamped
            Assert.StartsWith("[", line);
            Assert.Contains("] [INFO] ", line);
        }

        // ---------- Daily rollover ----------

        [Fact]
        public void EnsureWriter_OnSameDay_ReusesExistingWriter()
        {
            CliLogger.Info("first line");
            var firstFile = (string?)CurrentLogFileField.GetValue(null);
            CliLogger.Info("second line");
            var secondFile = (string?)CurrentLogFileField.GetValue(null);

            Assert.NotNull(firstFile);
            Assert.Equal(firstFile, secondFile);
        }

        // ---------- Independence ----------

        [Fact]
        public void LogDirectory_IsolatedFromProductionPath()
        {
            // The injected temp dir must never equal the real LocalAppData path.
            var productionPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitCliBridge", "Logs");
            Assert.NotEqual(productionPath, CliLogger.LogDirectory);
        }
    }
}
