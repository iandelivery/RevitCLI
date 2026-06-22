namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Interface for bridge command handlers. Implement this to create plugin commands.
    /// </summary>
    /// <remarks>
    /// The <paramref name="uiApplication"/> parameter in <see cref="Handle"/>
    /// is passed as <c>object</c> to avoid requiring Revit API references in the
    /// abstractions project. Cast it to <c>Autodesk.Revit.UI.UIApplication</c>
    /// inside your handler implementation.
    ///
    /// Metadata properties (Description, Category, Parameters, Aliases, SupportsDryRun)
    /// are declared here but have safe defaults in <see cref="BridgeCommandBase"/>.
    /// Existing implementations that inherit from <see cref="BridgeCommandBase"/>
    /// remain compatible without changes.
    /// </remarks>
    public interface IBridgeCommand
    {
        /// <summary>
        /// The command name that this handler responds to (e.g. "create_wall").
        /// </summary>
        string CommandName { get; }

        /// <summary>
        /// Execute the command on the Revit main thread.
        /// </summary>
        /// <param name="uiApplication">
        /// The Revit <c>UIApplication</c> instance, passed as <c>object</c>
        /// to avoid Revit API dependency. Cast to <c>Autodesk.Revit.UI.UIApplication</c> in your handler.
        /// </param>
        /// <param name="cmd">The queued command with task ID and parameters.</param>
        /// <returns>JSON string representing the command result (use <see cref="CommandResponse"/> to build).</returns>
        string Handle(object uiApplication, QueuedCommand cmd);

        /// <summary>
        /// Human-readable description of what this command does.
        /// Used by the schema discovery endpoint for agent self-correction.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Category for grouping commands (e.g. "Create", "Query", "System").
        /// </summary>
        string Category { get; }

        /// <summary>
        /// Parameter schema describing expected inputs.
        /// Enables agents to construct valid payloads without documentation.
        /// </summary>
        CommandParamSchema[] Parameters { get; }

        /// <summary>
        /// Alternative names for this command (e.g. ["wall_create"] for "create_wall").
        /// </summary>
        string[] Aliases { get; }

        /// <summary>
        /// Whether this command supports --dry-run mode (auto-rollback transaction).
        /// Read-only commands should return false.
        /// </summary>
        bool SupportsDryRun { get; }

        /// <summary>
        /// Example invocations for this command. Used by the schema discovery
        /// endpoint to help agents construct valid requests.
        /// Return an empty array if no examples are needed.
        /// </summary>
        string[] Examples { get; }
    }
}
