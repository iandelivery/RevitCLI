namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Lifecycle hook for bridge plugins that want structured registration
    /// and teardown semantics. A plugin implements this interface and is
    /// discovered by the bridge (via assembly reflection or explicit
    /// registration); the bridge then calls
    /// <see cref="OnRegistered"/> at startup and
    /// <see cref="OnUnregistered"/> at shutdown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This complements (rather than replaces) the simpler
    /// <c>CommandRouter.Register</c> call. Plugins that need resource
    /// cleanup on bridge shutdown — closing file handles, unsubscribing
    /// Revit events, flushing caches — should implement this interface.
    /// Plugins that only register a single stateless command can keep
    /// calling <c>Register</c> directly from their
    /// <c>IExternalApplication.OnStartup</c>.
    /// </para>
    /// <para>
    /// All methods are invoked on the Revit main thread during bridge
    /// initialization (<see cref="OnRegistered"/>) or teardown
    /// (<see cref="OnUnregistered"/>). They must not block; long work
    /// should be deferred to a background thread that does not touch the
    /// Revit API.
    /// </para>
    /// </remarks>
    public interface IBridgePlugin
    {
        /// <summary>
        /// Stable identifier for this plugin. Used in logs, metrics, and the
        /// <c>/api/commands</c> schema's <c>plugins</c> list. Should be
        /// unique across all installed plugins — a company prefix is
        /// recommended (e.g. <c>mycompany.walls</c>).
        /// </summary>
        string PluginId { get; }

        /// <summary>
        /// Called once after the bridge registry is ready. The plugin should
        /// register all its <see cref="IBridgeCommand"/> handlers via
        /// <paramref name="registry"/>. Throwing here fails plugin load but
        /// does not crash the bridge; the exception is logged.
        /// </summary>
        /// <param name="registry">
        /// The command registry to register handlers against. Equivalent to
        /// the static <c>CommandRouter</c> but injectable for testability.
        /// </param>
        void OnRegistered(ICommandRegistry registry);

        /// <summary>
        /// Called once when the bridge is shutting down (Revit closing or
        /// bridge disabled). The plugin should release resources and let the
        /// registry unregister its handlers, or call
        /// <see cref="ICommandRegistry.Unregister"/> explicitly. Must not
        /// throw — exceptions are swallowed and logged.
        /// </summary>
        /// <param name="registry">The same registry passed to <see cref="OnRegistered"/>.</param>
        void OnUnregistered(ICommandRegistry registry);
    }
}
