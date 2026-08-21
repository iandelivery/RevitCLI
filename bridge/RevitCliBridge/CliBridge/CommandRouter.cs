using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace RevitCliBridge
{
    /// <summary>
    /// Routes CLI commands to specific command handler implementations.
    /// Auto-discovers all IBridgeCommand implementations from the executing assembly.
    /// Uses lazy initialization instead of a static constructor to avoid
    /// TypeInitializationException that cannot be recovered from.
    /// </summary>
    public static class CommandRouter
    {
        private static readonly ConcurrentDictionary<string, IBridgeCommand> _handlers =
            new ConcurrentDictionary<string, IBridgeCommand>();

        /// <summary>
        /// Command name aliases — maps alternative names to the primary command name.
        /// </summary>
        private static readonly Dictionary<string, string> _aliases =
            new Dictionary<string, string>
            {
                { "unhide_elements", "hide_elements" }
            };

        private static bool _initialized;
        private static bool _initializing; // Re-entrancy guard for GetTypes() → type init → CommandRouter
        private static readonly object _initLock = new();

        /// <summary>
        /// Ensures handlers are discovered. Uses lazy initialization with error
        /// handling so a single bad handler doesn't make the entire type unusable.
        /// A re-entrancy guard prevents StackOverflow if Assembly.GetTypes()
        /// triggers a type initializer that calls back into CommandRouter.
        /// </summary>
        private static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;
                if (_initializing) return; // Re-entrant call during type discovery — break recursion

                _initializing = true;
                try
                {
                    // Auto-discover all IBridgeCommand implementations in the executing assembly
                    var handlerTypes = Assembly.GetExecutingAssembly()
                        .GetTypes()
                        .Where(t => typeof(IBridgeCommand).IsAssignableFrom(t)
                                 && !t.IsAbstract
                                 && !t.IsInterface);

                    foreach (var handlerType in handlerTypes)
                    {
                        try
                        {
                            var cmd = (IBridgeCommand)Activator.CreateInstance(handlerType);
                            // Register under the versioned key "{name}@{version}"
                            // so multiple versions can coexist. Unversioned
                            // lookups fall back to the default version.
                            var versionedKey = $"{cmd.CommandName}@{cmd.Version}";
                            Register(versionedKey, cmd);

                            // Also register the bare name as an alias pointing
                            // to the default version, so legacy callers that
                            // omit @version still resolve.
                            if (cmd.Version == CommandNameResolver.DefaultVersion)
                            {
                                _handlers[cmd.CommandName] = cmd;
                            }

                            // Auto-register aliases (also versioned).
                            foreach (var alias in cmd.Aliases)
                            {
                                _handlers[$"{alias}@{cmd.Version}"] = cmd;
                                if (cmd.Version == CommandNameResolver.DefaultVersion)
                                    _handlers[alias] = cmd;
                            }
                        }
                        catch (Exception ex)
                        {
                            CliLogger.Warn($"Failed to register command handler {handlerType.Name}: {ex.Message}");
                    }
                    }

                    // Register static aliases (legacy compatibility).
                    // Map alias → "{targetName}@v1" (default version).
                    foreach (var alias in _aliases)
                    {
                        var versionedTarget = $"{alias.Value}@{CommandNameResolver.DefaultVersion}";
                        if (_handlers.TryGetValue(versionedTarget, out var targetCmd))
                        {
                            _handlers[alias.Key] = targetCmd;
                        }
                    }

                    _initialized = true;
                }
                finally
                {
                    _initializing = false;
                }
            }
        }

        public static void Register(string commandName, IBridgeCommand handler)
        {
            EnsureInitialized();
            // During initialization, _handlers is already being populated by
            // EnsureInitialized(), so this direct add is safe and avoids
            // re-entrancy issues.
            _handlers[commandName] = handler;

            // Invalidate the HTTP server's schema cache so subsequent schema
            // requests reflect the newly registered handler. Skipped during
            // initial discovery (cache is not built yet at that point).
            if (_initialized)
            {
                CliHttpServer.InvalidateSchemaCache();
            }
        }

        /// <summary>
        /// Returns all registered primary command handlers (excludes alias entries).
        /// Used by the schema discovery endpoint to build command metadata.
        /// Deduplicates by (CommandName, Version) so a v1 and v2 handler of
        /// the same command both appear.
        /// </summary>
        public static IEnumerable<IBridgeCommand> GetAllHandlers()
        {
            EnsureInitialized();
            var seenKeys = new HashSet<string>();
            foreach (var kvp in _handlers)
            {
                // Deduplicate by CommandName+Version — alias and bare-name
                // entries point to the same handler instance.
                var dedupKey = $"{kvp.Value.CommandName}@{kvp.Value.Version}";
                if (seenKeys.Add(dedupKey))
                    yield return kvp.Value;
            }
        }

        /// <summary>
        /// Returns a specific handler by primary command name, or null if not found.
        /// </summary>
        public static IBridgeCommand? GetHandler(string commandName)
        {
            EnsureInitialized();
            _handlers.TryGetValue(commandName, out var handler);
            return handler;
        }

        public static string Execute(UIApplication app, QueuedCommand queuedCommand)
        {
            EnsureInitialized();

            // Resolve domain path notation (e.g. "elements.walls.create" → "create_wall")
            var resolvedCommand = ResolveCommandName(queuedCommand.Command);
            if (resolvedCommand != queuedCommand.Command)
            {
                queuedCommand = new QueuedCommand
                {
                    TaskId = queuedCommand.TaskId,
                    Command = resolvedCommand,
                    Parameters = queuedCommand.Parameters,
                    DryRun = queuedCommand.DryRun,
                    RequestId = queuedCommand.RequestId
                };
            }

            if (!_handlers.TryGetValue(queuedCommand.Command, out var handler))
            {
                return CommandResponse.Error(
                    queuedCommand.TaskId,
                    $"Unknown command: {queuedCommand.Command}").ToJson();
            }

            // Wire the progress channel so any handler (built-in or plugin)
            // can report via cmd.ReportProgress — routed to the same task
            // state machine that drives SSE "progress" events. Wired after
            // the domain-path rebuild above so the rebuilt instance carries
            // the reporter. TaskRegistry.SetProgress no-ops for task IDs
            // without a TaskInfo (e.g. batch sub-commands).
            queuedCommand.ProgressReporter =
                (percent, message) => TaskRegistry.SetProgress(queuedCommand.TaskId, percent, message);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool success = true;
            try
            {
                return handler.Handle(app, queuedCommand);
            }
            catch (Exception ex)
            {
                success = false;
                return CommandResponse.Error(
                    queuedCommand.TaskId,
                    $"Command '{queuedCommand.Command}' failed: {ex.Message}",
                    ex.ToString()).ToJson();
            }
            finally
            {
                sw.Stop();
                MetricsCollector.RecordCommand(queuedCommand.Command, sw.ElapsedMilliseconds, success);
                CliLogger.Info("command_executed",
                    ("command", queuedCommand.Command),
                    ("duration_ms", sw.ElapsedMilliseconds),
                    ("status", success ? "success" : "error"),
                    ("task_id", queuedCommand.TaskId));
            }
        }

        /// <summary>
        /// Resolves domain path notation to a command name.
        /// "elements.walls.create" → tries "elements.walls.create", then "walls.create", then "create"
        /// Also converts underscores: "wall_create" → tries "wall_create", then "create_wall"
        /// </summary>
        private static string ResolveCommandName(string input)
        {
            // Delegate to the pure-logic resolver so the routing rules can be
            // unit-tested without loading the executing assembly.
            return CommandNameResolver.Resolve(input, _handlers.Keys);
        }
    }
}
