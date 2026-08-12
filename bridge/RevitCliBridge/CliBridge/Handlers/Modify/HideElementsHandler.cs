using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class HideElementsHandler : DocumentCommandBase
    {
        public override string CommandName => "hide_elements";
        public override string Description => "Hides or unhides elements in a view";
        public override string Category => "UI";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_ids", Type = "int[]", Required = true, Description = "Element IDs to hide/unhide" },
            new CommandParamSchema { Name = "view_id", Type = "int", Required = false, Description = "View element ID (defaults to active view)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"hide_elements\", \"parameters\": { \"element_ids\": [123, 456] } }",
            "{ \"command\": \"hide_elements\", \"parameters\": { \"element_ids\": [123, 456], \"view_id\": 789 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            var elementIds = HandlerUtilities.GetIntArrayOrNull(parameters, "element_ids");
            if (elementIds is null || elementIds.Length == 0)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: element_ids.").ToJson();

            var view = ResolveView(doc, parameters);
            if (view is null)
                return CommandResponse.Error(cmd.TaskId, "No active view or specified view not found.").ToJson();

            var ids = elementIds.Select(id => new ElementId(id)).ToList();

            var validIds = new List<ElementId>();
            foreach (var eid in ids)
            {
                var elem = doc.GetElement(eid);
                if (elem is not null)
                    validIds.Add(eid);
            }

            if (validIds.Count == 0)
                return CommandResponse.Error(cmd.TaskId, "No valid elements found.").ToJson();

            string action = cmd.Command == "unhide_elements" ? "Unhide" : "Hide";

            using (var t = new Transaction(doc, $"CLI {action} Elements"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                try
                {
                    if (cmd.Command == "unhide_elements")
                        view.UnhideElements(validIds);
                    else
                        view.HideElements(validIds);
                }
                catch (Exception ex)
                {
                    return CommandResponse.Error(cmd.TaskId, $"Failed to {action.ToLower()} elements: {ex.Message}").ToJson();
                }

                t.Commit();
            }

            var result = new
            {
                view_id = view.Id.IntegerValue,
                view_name = view.Name,
                action = action.ToLower(),
                count = validIds.Count,
                element_ids = validIds.Select(id => id.IntegerValue).ToArray()
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"{action}d {validIds.Count} element(s) in view '{view.Name}'.").ToJson();
        }

        private View? ResolveView(Document doc, Dictionary<string, object> parameters)
        {
            int? viewId = HandlerUtilities.GetIntOrNull(parameters, "view_id");
            if (viewId is not null)
            {
                var viewElem = doc.GetElement(new ElementId(viewId.Value));
                if (viewElem is View v && !v.IsTemplate)
                    return v;
                return null;
            }

            return doc.ActiveView;
        }
    }
}
