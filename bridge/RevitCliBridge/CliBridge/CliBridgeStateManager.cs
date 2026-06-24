using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Models;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitCliBridge
{
    /// <summary>
    /// Manage CLI Bridge state.
    /// Provides start, stop, and toggle functionality for CLI Bridge.
    /// Supports dynamic port allocation and instance registry for multi-instance scenarios.
    /// </summary>
    public static class CliBridgeStateManager
    {
        private static readonly object _lock = new();
        private static CliHttpServer? _cliServer;
        private static ExternalEvent? _cliExternalEvent;
        private static volatile bool _isEnabled;
        private static int _revitVersion;
        private static int _actualPort;
        private static UIApplication? _uiApp;

        /// <summary>
        /// Get if CLI Bridge is enabled.
        /// </summary>
        public static bool IsEnabled => _isEnabled;

        /// <summary>
        /// Get external event instance for TaskRegistry.
        /// </summary>
        public static ExternalEvent? RevitEvent => _cliExternalEvent;

        /// <summary>
        /// Get the Revit version (year, e.g. 2022) of the running instance.
        /// </summary>
        public static int RevitVersion => _revitVersion;

        /// <summary>
        /// Get the actual port the bridge is listening on.
        /// May differ from config if auto_port is enabled.
        /// </summary>
        public static int ActualPort => _actualPort;

        /// <summary>
        /// Get the UIApplication reference (set by CliCommandHandler on first execution).
        /// </summary>
        public static UIApplication? UIApplication => _uiApp;

        /// <summary>
        /// Store the UIApplication reference for use by endpoints that need
        /// access to the active document (e.g. /api/llms.txt).
        /// Called by CliCommandHandler.Execute on each wake-up.
        /// </summary>
        public static void SetUIApplication(UIApplication app)
        {
            Volatile.Write(ref _uiApp, app);
        }

        /// <summary>
        /// Initialize CLI Bridge.
        /// Based on the enabled value in configuration, decide if to enable it by default.
        /// </summary>
        /// <param name="revitVersion">Revit version year (e.g. 2022). 0 if unknown.</param>
        public static void Initialize(int revitVersion = 0)
        {
            lock (_lock)
            {
                if (_isEnabled)
                {
                    CliLogger.Info("CLI Bridge is already enabled.");
                    return;
                }

                _revitVersion = revitVersion;
                var config = CliBridgeConfigLoader.Config;

                if (!config.Enabled)
                {
                    CliLogger.Info("CLI Bridge is disabled in configuration. Use the toggle command to enable it.");
                    _isEnabled = false;
                    return;
                }

                int port = ResolvePort(config);
                _isEnabled = StartBridge(port);
            }
        }

        /// <summary>
        /// Toggle CLI Bridge on/off.
        /// </summary>
        /// <returns>State after toggle (true = enabled, false = disabled)</returns>
        public static bool Toggle()
        {
            lock (_lock)
            {
                if (_isEnabled)
                {
                    CliLogger.Info($"Begin to disable CLI Bridge...");
                    bool stopped = Stop();
                    _isEnabled = !stopped;
                }
                else
                {
                    CliLogger.Info($"Begin to enable CLI Bridge...");
                    var config = CliBridgeConfigLoader.Config;
                    int port = ResolvePort(config);
                    _isEnabled = StartBridge(port);
                }
                return _isEnabled;
            }
        }

        /// <summary>
        /// Resolve the port to use based on configuration and Revit version.
        /// If auto_port is enabled and a version is known, dynamically allocates
        /// an available port from the version's range.
        /// </summary>
        private static int ResolvePort(CliBridgeConfig config)
        {
            if (config.AutoPort && _revitVersion > 0)
            {
                return PortAllocator.AllocatePort(_revitVersion, config.Port);
            }
            return config.Port;
        }

        /// <summary>
        /// Start CLI Bridge.
        /// </summary>
        private static bool StartBridge(int port)
        {
            try
            {
                if (_isEnabled)
                {
                    CliLogger.Info("CLI Bridge is already enabled.");
                    return false;
                }

                if (_cliServer != null)
                {
                    CliLogger.Warn("Found server instances; attempting to clean them up...");
                    try { _cliServer.Stop(); } catch { }
                    try { _cliServer.Dispose(); } catch { }
                    _cliServer = null;
                }

                // Clean up stale instance registry files from crashed Revit instances.
                InstanceRegistry.CleanupStale();

                var handler = new CliCommandHandler();
                _cliExternalEvent = ExternalEvent.Create(handler);
                TaskRegistry.RevitEvent = _cliExternalEvent;

                _cliServer = new CliHttpServer(port);
                int pid = Process.GetCurrentProcess().Id;
                _cliServer.SetIdentity(_revitVersion, pid);
                _cliServer.Start();
                _actualPort = _cliServer.Port;

                var pluginDir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(CliBridgeStateManager).Assembly.Location) ?? "",
                    "CliBridgePlugins");
                BridgePluginLoader.LoadPlugins(pluginDir);

                // Write instance registry file.
                InstanceRegistry.Register(new InstanceRegistry.InstanceInfo
                {
                    Pid = pid,
                    Version = _revitVersion,
                    Port = _actualPort,
                    Document = null,
                    StartedAt = DateTime.UtcNow,
                    Hostname = "localhost",
                    CommandsCount = CommandRouter.GetAllHandlers().Count()
                });

                CliLogger.Info($"CLI Bridge enabled on port {_actualPort} (Revit {_revitVersion}, PID {pid}).");
                return true;
            }
            catch (Exception ex)
            {
                CliLogger.Error($"Failed to enable CLI Bridge: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stop CLI Bridge.
        /// </summary>
        public static bool Stop()
        {
            if (!_isEnabled)
            {
                CliLogger.Info("CLI Bridge is already disabled.");
                return false;
            }

            try
            {
                _cliServer?.Stop();
            }
            catch (Exception ex)
            {
                CliLogger.Warn($"Error stopping CLI server: {ex.Message}");
                return false;
            }

            bool disposed = false;
            try
            {
                _cliServer?.Dispose();
                disposed = true;
            }
            catch (Exception ex)
            {
                CliLogger.Error($"Error disposing CLI server: {ex.Message}");
                return false;
            }

            if (disposed)
            {
                // Remove instance registry file.
                InstanceRegistry.Unregister();

                _cliServer = null;
                _cliExternalEvent = null;
                TaskRegistry.RevitEvent = null;
                _actualPort = 0;
                CliLogger.Info("CLI Bridge disabled.");
                return true;
            }
            else
            {
                CliLogger.Error("CLI Bridge failed to stop completely; state remains enabled.");
                return false;
            }
        }

        /// <summary>
        /// Complete cleanup of CLI Bridge resources.
        /// </summary>
        public static void Cleanup()
        {
            lock (_lock)
            {
                _ = Stop();
            }
        }

        /// <summary>
        /// Update the active document name in the instance registry.
        /// Called when the active document changes in Revit.
        /// </summary>
        public static void UpdateActiveDocument(string? documentName)
        {
            InstanceRegistry.UpdateDocument(documentName);
        }
    }
}
