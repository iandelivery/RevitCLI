using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Views
{
    public class DuplicateViewHandler : DocumentCommandBase
    {
        public override string CommandName => "duplicate_view";
        public override string Description => "Duplicates a view (optionally with detailing, as dependent, with a new name, template, or activation)";
        public override string Category => "Create";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "view_id", Type = "int", Required = true, Description = "Element ID of the view to duplicate" },
            new CommandParamSchema { Name = "mode", Type = "string", Required = false, Description = "Duplicate option", EnumValues = new[] { "duplicate", "with_detailing", "as_dependent" }, Default = "duplicate" },
            new CommandParamSchema { Name = "name", Type = "string", Required = false, Description = "Name for the duplicated view" },
            new CommandParamSchema { Name = "template_id", Type = "int", Required = false, Description = "View template element ID to apply to the duplicate" },
            new CommandParamSchema { Name = "activate", Type = "bool", Required = false, Description = "Set the duplicated view as the active view", Default = false }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"duplicate_view\", \"parameters\": { \"view_id\": 12345 } }",
            "{ \"command\": \"duplicate_view\", \"parameters\": { \"view_id\": 12345, \"mode\": \"with_detailing\", \"name\": \"Level 1 - Copy\" } }",
            "{ \"command\": \"duplicate_view\", \"parameters\": { \"view_id\": 12345, \"mode\": \"as_dependent\", \"activate\": true } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            int? viewId = HandlerUtilities.GetIntOrNull(parameters, "view_id");
            if (!viewId.HasValue)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: view_id.").ToJson();

            var sourceView = doc.GetElement(new ElementId(viewId.Value)) as View;
            if (sourceView is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {viewId.Value} is not a view.").ToJson();

            string? modeStr = HandlerUtilities.GetStringOrNull(parameters, "mode") ?? "duplicate";
            var option = modeStr switch
            {
                "with_detailing" => ViewDuplicateOption.WithDetailing,
                "as_dependent" => ViewDuplicateOption.AsDependent,
                _ => ViewDuplicateOption.Duplicate
            };

            if (!sourceView.CanViewBeDuplicated(option))
                return CommandResponse.Error(cmd.TaskId,
                    $"View '{sourceView.Name}' cannot be duplicated with option '{modeStr}'.").ToJson();

            string? newName = HandlerUtilities.GetStringOrNull(parameters, "name");
            int? templateId = HandlerUtilities.GetIntOrNull(parameters, "template_id");
            bool activate = false;
            if (parameters.TryGetValue("activate", out var activateObj))
            {
                try { activate = Convert.ToBoolean(activateObj); } catch { }
            }

            View newView;

            using (var t = new DryRunTransaction(doc, "CLI Duplicate View", cmd.DryRun))
            {
                t.ConfigureFailureHandling();

                try
                {
                    var newViewId = sourceView.Duplicate(option);
                    newView = (View)doc.GetElement(newViewId);

                    if (!string.IsNullOrEmpty(newName))
                    {
                        try { newView.Name = newName; } catch { }
                    }

                    if (templateId.HasValue && templateId.Value > 0)
                    {
                        if (doc.GetElement(new ElementId(templateId.Value)) is View tpl && tpl.IsTemplate)
                            newView.ViewTemplateId = tpl.Id;
                    }
                }
                catch (Exception ex)
                {
                    // Dispose rolls back the uncommitted transaction.
                    return CommandResponse.Error(cmd.TaskId, $"Failed to duplicate view: {ex.Message}").ToJson();
                }

                t.Commit();
            }

            if (activate)
            {
                var uiDoc = app.ActiveUIDocument;
                if (uiDoc is not null)
                    uiDoc.ActiveView = newView;
            }

            var result = new
            {
                new_view_id = newView.Id.IntegerValue,
                name = newView.Name,
                mode = modeStr,
                activated = activate
            };

            return CommandResponse.Success(cmd.TaskId, result, $"View duplicated as '{newView.Name}'.").ToJson();
        }
    }
}
