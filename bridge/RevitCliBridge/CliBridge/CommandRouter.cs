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
                            Register(cmd.CommandName, cmd);

                            // Auto-register aliases from handler's Aliases property
                            foreach (var alias in cmd.Aliases)
                            {
                                _handlers[alias] = cmd;
                            }
                        }
                        catch (Exception ex)
                        {
                            CliLogger.Warn($"Failed to register command handler {handlerType.Name}: {ex.Message}");
                        }
                    }

                    // Register static aliases (legacy compatibility)
                    foreach (var alias in _aliases)
                    {
                        if (_handlers.TryGetValue(alias.Value, out var targetCmd))
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
        }

        /// <summary>
        /// Returns all registered primary command handlers (excludes alias entries).
        /// Used by the schema discovery endpoint to build command metadata.
        /// </summary>
        public static IEnumerable<IBridgeCommand> GetAllHandlers()
        {
            EnsureInitialized();
            var seenNames = new HashSet<string>();
            foreach (var kvp in _handlers)
            {
                // Skip alias entries — they point to the same handler instance
                if (seenNames.Add(kvp.Value.CommandName))
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
                    DryRun = queuedCommand.DryRun
                };
            }

            if (!_handlers.TryGetValue(queuedCommand.Command, out var handler))
            {
                return CommandResponse.Error(
                    queuedCommand.TaskId,
                    $"Unknown command: {queuedCommand.Command}").ToJson();
            }

            try
            {
                return handler.Handle(app, queuedCommand);
            }
            catch (Exception ex)
            {
                return CommandResponse.Error(
                    queuedCommand.TaskId,
                    $"Command '{queuedCommand.Command}' failed: {ex.Message}",
                    ex.ToString()).ToJson();
            }
        }

        /// <summary>
        /// Resolves domain path notation to a command name.
        /// "elements.walls.create" → tries "elements.walls.create", then "walls.create", then "create"
        /// Also converts underscores: "wall_create" → tries "wall_create", then "create_wall"
        /// </summary>
        private static string ResolveCommandName(string input)
        {
            if (_handlers.ContainsKey(input)) return input;

            // Domain path: try progressively shorter suffixes
            if (input.Contains("."))
            {
                var parts = input.Split('.');
                for (int i = 1; i < parts.Length; i++)
                {
                    var candidate = string.Join(".", parts, i, parts.Length - i);
                    if (_handlers.ContainsKey(candidate)) return candidate;
                }

                // Try last segment only
                var lastSegment = parts[parts.Length - 1];
                if (_handlers.ContainsKey(lastSegment)) return lastSegment;
            }

            // Underscore reversal: "wall_create" → "create_wall"
            if (input.Contains("_"))
            {
                var parts = input.Split('_');
                if (parts.Length == 2)
                {
                    var reversed = $"{parts[1]}_{parts[0]}";
                    if (_handlers.ContainsKey(reversed)) return reversed;
                }
            }

            return input;
        }
    }
}
