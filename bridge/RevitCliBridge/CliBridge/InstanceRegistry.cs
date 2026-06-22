using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RevitCliBridge
{
    /// <summary>
    /// Manages instance registry files in %AppData%\revit-cli\instances\.
    /// Each running bridge writes a JSON file on startup and deletes it on shutdown.
    /// Enables the CLI client to discover running Revit instances.
    /// </summary>
    public static class InstanceRegistry
    {
        private static readonly string InstancesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "revit-cli", "instances");

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
        /// </summary>
        public static void Register(InstanceInfo info)
        {
            try
            {
                Directory.CreateDirectory(InstancesDir);
                _currentRegistryFile = Path.Combine(InstancesDir,
                    $"revit-{info.Version}-{info.Pid}.json");

                var json = JsonConvert.SerializeObject(info, Formatting.Indented);
                File.WriteAllText(_currentRegistryFile, json);

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
                // Process.GetProcessById succeeds if the process exists,
                // but it might have exited between the call and now.
                return !process.HasExited;
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
    }
}
