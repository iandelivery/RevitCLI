using System;
using System.IO;
using System.Text;

namespace RevitCliBridge
{
    /// <summary>
    /// Simple structured logger for CLI bridge operations. Writes to daily
    /// log files under %LOCALAPPDATA%/RevitCliBridge/Logs with a 4KB buffered
    /// StreamWriter (flushed on Shutdown or daily rollover). Log lines use a
    /// key=value structured format so they can be grep-filtered by field.
    /// </summary>
    public static class CliLogger
    {
        private static readonly object _lock = new object();
        private static string? _logDirectory;
        private static StreamWriter? _writer;
        private static string? _currentLogFile;
        private static readonly StringBuilder _fieldBuffer = new StringBuilder(256);

        public static string LogDirectory
        {
            get
            {
                if (_logDirectory == null)
                {
                    _logDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RevitCliBridge", "Logs");
                    Directory.CreateDirectory(_logDirectory);
                }
                return _logDirectory;
            }
        }

        public static void Info(string message, params (string key, object value)[] fields)
            => Log("INFO", message, fields);

        public static void Error(string message, params (string key, object value)[] fields)
            => Log("ERROR", message, fields);

        public static void Warn(string message, params (string key, object value)[] fields)
            => Log("WARN", message, fields);

        /// <summary>
        /// Backward-compatible overloads for callers that don't have structured
        /// fields. Equivalent to passing an empty fields array.
        /// </summary>
        public static void Info(string message) => Log("INFO", message, null);
        public static void Error(string message) => Log("ERROR", message, null);
        public static void Warn(string message) => Log("WARN", message, null);

        private static void Log(string level, string message, (string key, object value)[]? fields)
        {
            string logLine = FormatLine(level, message, fields);
            System.Diagnostics.Debug.WriteLine(logLine);

            lock (_lock)
            {
                try
                {
                    EnsureWriter();
                    _writer?.WriteLine(logLine);
                    // Do NOT auto-flush per line — rely on the 4KB buffer.
                    // Flush happens on daily rollover or Shutdown().
                }
                catch
                {
                    // Swallow logging failures — never crash the bridge for logs.
                }
            }
        }

        private static string FormatLine(string level, string message, (string key, object value)[]? fields)
        {
            if (fields == null || fields.Length == 0)
                return $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

            _fieldBuffer.Clear();
            _fieldBuffer.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}");
            foreach (var (key, value) in fields)
            {
                _fieldBuffer.Append(' ').Append(key).Append('=');
                AppendValue(_fieldBuffer, value);
            }
            return _fieldBuffer.ToString();
        }

        private static void AppendValue(StringBuilder sb, object value)
        {
            // Quote strings containing spaces; leave numbers/bools bare.
            if (value is string s)
            {
                if (s.Contains(" ") || s.Contains("\""))
                    sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(s);
            }
            else
            {
                sb.Append(value);
            }
        }

        /// <summary>
        /// Lazily opens the StreamWriter for today's log file. If the date has
        /// rolled over since the last write, flushes and closes the old writer
        /// before opening a new one.
        /// </summary>
        private static void EnsureWriter()
        {
            string today = $"cli_bridge_{DateTime.Now:yyyy-MM-dd}.log";
            string targetFile = Path.Combine(LogDirectory, today);

            if (_writer != null && _currentLogFile == targetFile)
                return; // still on the same day's file

            // Day rollover (or first open): flush old writer, open new one.
            _writer?.Flush();
            _writer?.Dispose();

            // append=true so restarts don't clobber prior logs
            _writer = new StreamWriter(targetFile, append: true, Encoding.UTF8)
            {
                AutoFlush = false
            };
            _currentLogFile = targetFile;
        }

        /// <summary>
        /// Flushes the buffered writer and closes the file. Call on bridge
        /// shutdown to ensure no log lines are lost.
        /// </summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                try
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                }
                catch { }
                _writer = null;
                _currentLogFile = null;
            }
        }
    }
}
