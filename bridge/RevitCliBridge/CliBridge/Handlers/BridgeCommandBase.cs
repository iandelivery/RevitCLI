using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    /// <summary>
    /// Base class for all bridge command handlers.
    /// Handles the UIApplication cast from the object parameter.
    /// Provides safe defaults for <see cref="IBridgeCommand"/> metadata properties
    /// so existing handlers remain compatible without changes.
    /// </summary>
    public abstract class BridgeCommandBase : IBridgeCommand
    {
        public abstract string CommandName { get; }

        /// <summary>
        /// Command description. Subclasses can override to provide a specific description
        /// for the schema discovery endpoint.
        /// </summary>
        public virtual string Description => string.Empty;

        /// <summary>
        /// Command category (e.g. "Create", "Query", "System").
        /// </summary>
        public virtual string Category => "General";

        /// <summary>
        /// Parameter schema describing the expected input parameters.
        /// Subclasses can override to provide parameter metadata for Agent auto-discovery.
        /// </summary>
        public virtual CommandParamSchema[] Parameters => Array.Empty<CommandParamSchema>();

        /// <summary>
        /// Command aliases (e.g. "wall_create" -> "create_wall").
        /// </summary>
        public virtual string[] Aliases => Array.Empty<string>();

        /// <summary>
        /// Whether --dry-run mode is supported (auto-rollback transaction).
        /// Read-only commands should return false.
        /// </summary>
        public virtual bool SupportsDryRun => false;

        /// <summary>
        /// Example invocations for this command. Override to provide usage examples
        /// that help agents construct valid requests.
        /// </summary>
        public virtual string[] Examples => Array.Empty<string>();

        public string Handle(object uiApplication, QueuedCommand cmd)
        {
            if (uiApplication is not UIApplication app)
                return CommandResponse.Error(cmd.TaskId,
                    $"Invalid UIApplication parameter: expected UIApplication, got {(uiApplication == null ? "null" : uiApplication.GetType().Name)}").ToJson();

            if (cmd.DryRun && !SupportsDryRun)
                return CommandResponse.Error(cmd.TaskId,
                    $"Command '{CommandName}' does not support --dry-run").ToJson();

            return Execute(app, cmd);
        }

        protected abstract string Execute(UIApplication app, QueuedCommand cmd);
    }

    /// <summary>
    /// Base class for command handlers that require an active document.
    /// Provides document null-check and parameter parsing boilerplate.
    /// </summary>
    public abstract class DocumentCommandBase : BridgeCommandBase
    {
        protected sealed override string Execute(UIApplication app, QueuedCommand cmd)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc is null)
                return CommandResponse.Error(cmd.TaskId, "No active document.").ToJson();

            var parameters = cmd.Parameters as Dictionary<string, object> ?? new Dictionary<string, object>();
            return Execute(app, doc, parameters, cmd);
        }

        protected abstract string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd);
    }
}
