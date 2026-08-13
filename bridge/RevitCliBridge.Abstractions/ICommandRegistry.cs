using System.Collections.Generic;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Abstraction over the bridge's command registry. The built-in
    /// <c>CommandRouter</c> implements this interface; third-party plugins
    /// can depend on the interface instead of the concrete static class so
    /// they remain testable and decoupled from the bridge implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations must be thread-safe: <see cref="Register"/> and
    /// <see cref="Unregister"/> may be called from any addin's
    /// <c>OnStartup</c>/<c>OnShutdown</c> at any time, while
    /// <see cref="GetHandler"/> is invoked on the HTTP listener thread.
    /// </para>
    /// <para>
    /// Command name keys follow the versioned convention
    /// <c>{name}@{version}</c> (e.g. <c>create_wall@v1</c>). Callers that
    /// omit the version suffix resolve to the default version.
    /// </para>
    /// </remarks>
    public interface ICommandRegistry
    {
        /// <summary>
        /// Register (or replace) a command handler under the given key.
        /// </summary>
        /// <param name="commandName">
        /// Versioned key, e.g. <c>mycompany_tool@v1</c>. For the default
        /// version, the bare name may also be used to register an alias.
        /// </param>
        /// <param name="handler">The handler instance.</param>
        void Register(string commandName, IBridgeCommand handler);

        /// <summary>
        /// Remove a handler by its versioned key.
        /// </summary>
        /// <returns>
        /// <c>true</c> if a handler was removed; <c>false</c> if the key was
        /// not registered. Implementations should be safe to call while a
        /// command is executing (removal only affects subsequent lookups).
        /// </returns>
        bool Unregister(string commandName);

        /// <summary>
        /// Look up a handler by name or <c>name@version</c>.
        /// </summary>
        /// <returns>The handler, or <c>null</c> if not found.</returns>
        IBridgeCommand? GetHandler(string commandName);

        /// <summary>
        /// Read-only snapshot of all registered handlers keyed by versioned
        /// name. The returned dictionary must not be mutated by callers; it
        /// may be a live view or an immutable copy at the implementation's
        /// discretion.
        /// </summary>
        IReadOnlyDictionary<string, IBridgeCommand> Handlers { get; }
    }
}
