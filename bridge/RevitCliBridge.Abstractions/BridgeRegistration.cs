using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Programmatic registration entry point for plugin authors who want to
    /// register commands from their own Revit add-in using only this
    /// package — no reference to the bridge assembly, no
    /// <c>CliBridgePlugins</c> DLL-discovery folder (and therefore no
    /// Authenticode requirement).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bridge maintains its own static <c>CommandRouter</c> registry.
    /// This facade locates the already-loaded <c>RevitCliBridge</c> assembly
    /// in the current AppDomain and forwards registrations to
    /// <c>CommandRouter.Register(string, IBridgeCommand)</c> via reflection.
    /// Reflection is used because consumers deliberately do not reference
    /// the bridge assembly: shipping a second copy of it would split type
    /// identity inside the Revit process.
    /// </para>
    /// <para>
    /// Because the bridge assembly must already be loaded, the calling
    /// add-in must start after the bridge. Revit loads add-in manifests
    /// alphabetically, so name the manifest so it sorts after the bridge's
    /// own manifest.
    /// </para>
    /// </remarks>
    public static class BridgeRegistration
    {
        private const string BridgeAssemblyName = "RevitCliBridge";
        private const string RouterTypeName = "RevitCliBridge.CommandRouter";

        private static MethodInfo? _registerMethod;
        private static readonly object Gate = new object();

        /// <summary>
        /// Register a command under its versioned key (<c>{name}@{version}</c>),
        /// the bare name (when it is the default version), and every alias —
        /// the same set of keys the bridge's built-in auto-discovery would
        /// create. This is the overload most plugin authors want.
        /// </summary>
        public static void Register(IBridgeCommand command)
        {
            if (command is null) throw new ArgumentNullException(nameof(command));

            foreach (var key in GetRegistrationKeys(command))
            {
                Register(key, command);
            }
        }

        /// <summary>
        /// Register a command under one explicit registry key (advanced).
        /// Use <see cref="Register(IBridgeCommand)"/> unless you need custom
        /// key behavior.
        /// </summary>
        /// <param name="key">Registry key, e.g. <c>mycommand@v1</c>.</param>
        /// <param name="command">The handler to register.</param>
        public static void Register(string key, IBridgeCommand command)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Registry key must not be empty.", nameof(key));
            if (command is null) throw new ArgumentNullException(nameof(command));

            var register = ResolveRegisterMethod();
            try
            {
                register.Invoke(null, new object?[] { key, command });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                // Surface the bridge's own exception (e.g. registration
                // errors) instead of the reflection wrapper.
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // unreachable
            }
        }

        /// <summary>
        /// Computes the registry keys a command would be registered under:
        /// the versioned key <c>{name}@{version}</c>, the bare name when the
        /// command is the default version, and the same pair for every alias.
        /// Public so plugin authors can predict routing behavior and tests
        /// can assert on the key set.
        /// </summary>
        public static IReadOnlyList<string> GetRegistrationKeys(IBridgeCommand command)
        {
            if (command is null) throw new ArgumentNullException(nameof(command));
            if (string.IsNullOrWhiteSpace(command.CommandName))
                throw new ArgumentException("CommandName must not be empty.", nameof(command));

            var keys = new List<string>();
            var isDefault = command.Version == CommandNameResolver.DefaultVersion;

            keys.Add($"{command.CommandName}@{command.Version}");
            if (isDefault)
                keys.Add(command.CommandName);

            foreach (var alias in command.Aliases ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(alias)) continue;
                keys.Add($"{alias}@{command.Version}");
                if (isDefault)
                    keys.Add(alias);
            }

            return keys;
        }

        /// <summary>
        /// Locates <c>CommandRouter.Register(string, IBridgeCommand)</c> on the
        /// loaded bridge assembly and caches the <see cref="MethodInfo"/>.
        /// </summary>
        private static MethodInfo ResolveRegisterMethod()
        {
            lock (Gate)
            {
                if (_registerMethod is not null) return _registerMethod;

                var bridge = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, BridgeAssemblyName, StringComparison.Ordinal));

                if (bridge is null)
                    throw new InvalidOperationException(
                        "The RevitCliBridge assembly is not loaded in this process. " +
                        "Commands can only be registered while the bridge add-in is running. " +
                        "Ensure the bridge add-in is installed and that your add-in starts after it " +
                        "(Revit loads .addin manifests alphabetically).");

                var router = bridge.GetType(RouterTypeName, throwOnError: false);
                var register = router?.GetMethod(
                    "Register",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    new[] { typeof(string), typeof(IBridgeCommand) },
                    modifiers: null);

                if (register is null)
                    throw new InvalidOperationException(
                        $"Could not find CommandRouter.Register(string, IBridgeCommand) on the loaded {BridgeAssemblyName} assembly. " +
                        "The installed bridge version may be too old; upgrade it to match this package version.");

                _registerMethod = register;
                return register;
            }
        }
    }
}
