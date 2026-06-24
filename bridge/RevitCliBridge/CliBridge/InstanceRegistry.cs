using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RevitCliBridge
{
    /// <summary>
    /// Manages instance registry files in the revit-cli data directory.
    /// Each running bridge writes a JSON file on startup and deletes it on shutdown.
    /// Enables the CLI client to discover running Revit instances.
    /// </summary>
    public static class InstanceRegistry
    {
        private static readonly string InstancesDir = ResolveInstancesDir();

        private static string? _currentRegistryFile;

        /// <summary>
        /// Instance info written to the registry file.
        /// </summary>
        public class InstanceInfo
        {
            [JsonProperty("pid")]
            public int Pid { get; set; }

            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("port")]
            public int Port { get; set; }

            [JsonProperty("document")]
            public string? Document { get; set; }

            [JsonProperty("started_at")]
            public DateTime StartedAt { get; set; }

            [JsonProperty("hostname")]
            public string Hostname { get; set; } = "localhost";

            [JsonProperty("commands_count")]
            public int CommandsCount { get; set; }
        }

        /// <summary>
        /// Clean up stale registry files left by crashed Revit instances.
        /// Called on bridge startup before writing a new registry file.
        /// </summary>
        public static void CleanupStale()
        {
            try
            {
                if (!Directory.Exists(InstancesDir))
                    return;

                foreach (var file in Directory.GetFiles(InstancesDir, "revit-*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var info = JsonConvert.DeserializeObject<InstanceInfo>(json);
                        if (info == null) continue;

                        // Check if the PID is still alive.
                        if (!IsProcessAlive(info.Pid))
                        {
                            CliLogger.Info($"Cleaning stale registry file: {Path.GetFileName(file)} (PID {info.Pid} no longer exists)");
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        CliLogger.Warn($"Error checking registry file {Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                CliLogger.Warn($"Error during stale instance cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Write a registry file for this running instance.
        /// Uses atomic write (temp file + rename) to prevent corruption on crash.
        /// </summary>
        public static void Register(InstanceInfo info)
        {
            try
            {
                Directory.CreateDirectory(InstancesDir);
                _currentRegistryFile = Path.Combine(InstancesDir,
                    $"revit-{info.Version}-{info.Pid}.json");

                var json = JsonConvert.SerializeObject(info, Formatting.Indented);

                // Atomic write: write to temp file, then rename.
                var tmpFile = _currentRegistryFile + ".tmp";
                File.WriteAllText(tmpFile, json);
                File.Delete(_currentRegistryFile); // Delete existing (File.Move can't overwrite on all platforms)
                File.Move(tmpFile, _currentRegistryFile);

                CliLogger.Info($"Instance registered: {_currentRegistryFile}");
            }
            catch (Exception ex)
            {
                CliLogger.Warn($"Failed to write instance registry file: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete the registry file for this instance (called on shutdown).
        /// </summary>
        public static void Unregister()
        {
            if (_currentRegistryFile == null) return;

            try
            {
                if (File.Exists(_currentRegistryFile))
                {
                    File.Delete(_currentRegistryFile);
                    CliLogger.Info($"Instance registry file deleted: {_currentRegistryFile}");
                }
            }
            catch (Exception ex)
            {
                CliLogger.Warn($"Failed to delete instance registry file: {ex.Message}");
            }
            finally
            {
                _currentRegistryFile = null;
            }
        }

        /// <summary>
        /// Update the document field in the registry file.
        /// Called when the active document changes.
        /// </summary>
        public static void UpdateDocument(string? documentName)
        {
            if (_currentRegistryFile == null || !File.Exists(_currentRegistryFile))
                return;

            try
            {
                var json = File.ReadAllText(_currentRegistryFile);
                var info = JsonConvert.DeserializeObject<InstanceInfo>(json);
                if (info == null) return;

                info.Document = documentName;
                File.WriteAllText(_currentRegistryFile, JsonConvert.SerializeObject(info, Formatting.Indented));
            }
            catch (Exception ex)
            {
                CliLogger.Warn($"Failed to update instance registry document: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a process with the given PID is still alive.
        /// </summary>
        private static bool IsProcessAlive(int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                bool alive = !process.HasExited;
                process.Close(); // Release the process handle to avoid leaks.
                return alive;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Resolves the instances directory using a cascading strategy that
        /// mirrors the Go client's DataDir() logic so both sides agree on
        /// the same path:
        ///   1. REVIT_CLI_DATA_DIR environment variable (explicit override)
        ///   2. %LOCALAPPDATA%\revit-cli\instances (best Windows practice)
        ///   3. %USERPROFILE%\.revit-cli\instances (CLI dot-folder fallback)
        ///   4. %APPDATA%\revit-cli\instances (legacy fallback)
        /// </summary>
        private static string ResolveInstancesDir()
        {
            // 1. Explicit override.
            var envOverride = Environment.GetEnvironmentVariable("REVIT_CLI_DATA_DIR");
            if (!string.IsNullOrEmpty(envOverride))
                return Path.Combine(envOverride, "instances");

            // 2. Local AppData — best Windows practice for local app data.
            try
            {
                var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (!string.IsNullOrEmpty(localAppData))
                {
                    var dir = Path.Combine(localAppData, "revit-cli", "instances");
                    Directory.CreateDirectory(dir);
                    return dir;
                }
            }
            catch { /* suppress and fallback */ }

            // 3. User profile dot-folder — standard CLI convention.
            try
            {
                var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
                if (!string.IsNullOrEmpty(userProfile))
                {
                    var dir = Path.Combine(userProfile, ".revit-cli", "instances");
                    Directory.CreateDirectory(dir);
                    return dir;
                }
            }
            catch { /* suppress and fallback */ }

            // 4. Legacy fallback — %AppData%\revit-cli\instances.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "revit-cli", "instances");
        }
    }
}
