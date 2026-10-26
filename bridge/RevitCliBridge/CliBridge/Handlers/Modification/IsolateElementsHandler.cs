using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Modification
{
    public class IsolateElementsHandler : DocumentCommandBase
    {
        public override string CommandName => "isolate_elements";
        public override string Description => "Temporarily isolates elements in a view (all other elements are hidden)";
        public override string Category => "UI";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_ids", Type = "int[]", Required = true, Description = "Element IDs to isolate" },
            new CommandParamSchema { Name = "view_id", Type = "int", Required = false, Description = "View element ID (defaults to active view)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"isolate_elements\", \"parameters\": { \"element_ids\": [123, 456] } }",
            "{ \"command\": \"isolate_elements\", \"parameters\": { \"element_ids\": [123, 456], \"view_id\": 789 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var elementIds = HandlerUtilities.GetIntArrayOrNull(parameters, "element_ids");
            if (elementIds is null || elementIds.Length == 0)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: element_ids.").ToJson();

            var view = ResolveView(doc, parameters);
            if (view is null)
                return CommandResponse.Error(cmd.TaskId, "No active view or specified view not found.").ToJson();

            var validIds = new List<ElementId>();
            foreach (var id in elementIds)
            {
                var element = doc.GetElement(new ElementId(id));
                if (element is not null)
                    validIds.Add(element.Id);
            }

            if (validIds.Count == 0)
                return CommandResponse.Error(cmd.TaskId, "No valid elements found.").ToJson();

            using (var t = new DryRunTransaction(doc, "CLI Isolate Elements", cmd.DryRun))
            {
                t.ConfigureFailureHandling();
                view.IsolateElementsTemporary(validIds);
                t.Commit();
            }

            var result = new
            {
                view_id = view.Id.IntegerValue,
                view_name = view.Name,
                count = validIds.Count,
                element_ids = validIds.Select(id => id.IntegerValue).ToArray()
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Isolated {validIds.Count} element(s) in view '{view.Name}'.").ToJson();
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
