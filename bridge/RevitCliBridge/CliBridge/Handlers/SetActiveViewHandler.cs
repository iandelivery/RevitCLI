using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class SetActiveViewHandler : DocumentCommandBase
    {
        public override string CommandName => "set_active_view";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var uiDoc = app.ActiveUIDocument;
            if (uiDoc is null)
                return CommandResponse.Error(cmd.TaskId, "No active document.").ToJson();

            int? viewId = HandlerUtilities.GetIntOrNull(parameters, "view_id");
            string? viewName = HandlerUtilities.GetStringOrNull(parameters, "view_name");

            if (viewId is null && string.IsNullOrEmpty(viewName))
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: view_id or view_name.").ToJson();

            View? targetView = null;

            if (viewId is not null)
            {
                var element = doc.GetElement(new ElementId(viewId.Value));
                if (element is View view)
                {
                    targetView = view;
                }
                else
                {
                    return CommandResponse.Error(cmd.TaskId, $"Element with ID {viewId.Value} is not a view.").ToJson();
                }
            }
            else if (!string.IsNullOrEmpty(viewName))
            {
                var collector = new FilteredElementCollector(doc).OfClass(typeof(View));
                var views = collector.Cast<View>().Where(v => !v.IsTemplate && v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (views.Count == 0)
                {
                    return CommandResponse.Error(cmd.TaskId, $"View with name '{viewName}' not found.").ToJson();
                }
                else if (views.Count > 1)
                {
                    var viewList = views.Select(v => new { id = v.Id.IntegerValue, name = v.Name, type = v.ViewType.ToString() }).ToList();
                    return CommandResponse.Error(cmd.TaskId, $"Multiple views found with name '{viewName}'. Use view_id instead.", JsonConvert.SerializeObject(viewList)).ToJson();
                }

                targetView = views[0];
            }

            if (targetView is null)
                return CommandResponse.Error(cmd.TaskId, "Target view not found.").ToJson();

            uiDoc.RequestViewChange(targetView);

            var result = new
            {
                view_id = targetView.Id.IntegerValue,
                view_name = targetView.Name,
                view_type = targetView.ViewType.ToString()
            };

            return CommandResponse.Success(cmd.TaskId, result, "Active view changed successfully.").ToJson();
        }
    }
}
