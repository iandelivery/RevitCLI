using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Modification
{
    public class ClearTemporaryIsolateHandler : DocumentCommandBase
    {
        public override string CommandName => "clear_temporary_isolate";
        public override string Description => "Clears the temporary hide/isolate mode in a view";
        public override string Category => "UI";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "view_id", Type = "int", Required = false, Description = "View element ID (defaults to active view)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"clear_temporary_isolate\", \"parameters\": {} }",
            "{ \"command\": \"clear_temporary_isolate\", \"parameters\": { \"view_id\": 789 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var view = ResolveView(doc, parameters);
            if (view is null)
                return CommandResponse.Error(cmd.TaskId, "No active view or specified view not found.").ToJson();

            if (!view.CanUseTemporaryVisibilityModes())
                return CommandResponse.Error(cmd.TaskId,
                    $"View '{view.Name}' does not support temporary visibility modes.").ToJson();

            using (var t = new Transaction(doc, "CLI Clear Temporary Isolate"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                // Safe no-op when temporary hide/isolate is not active.
                view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

                t.Commit();
            }

            var result = new
            {
                view_id = view.Id.IntegerValue,
                view_name = view.Name,
                cleared = true
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Temporary hide/isolate cleared in view '{view.Name}'.").ToJson();
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
