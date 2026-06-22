using System;
using System.IO;

namespace RevitCliBridge
{
    /// <summary>
    /// Simple logger for CLI bridge operations.
    /// </summary>
    public static class CliLogger
    {
        private static readonly object _lock = new object();
        private static string? _logDirectory;

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

        public static void Info(string message) => Log("INFO", message);
        public static void Error(string message) => Log("ERROR", message);
        public static void Warn(string message) => Log("WARN", message);

        private static void Log(string level, string message)
        {
            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

            System.Diagnostics.Debug.WriteLine(logLine);

            lock (_lock)
            {
                try
                {
                    string logFile = Path.Combine(LogDirectory, $"cli_bridge_{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(logFile, logLine + Environment.NewLine);
                }
                catch
                {
                }
            }
        }
    }
}
