using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Query
{
    public class GetSelectedElementsHandler : DocumentCommandBase
    {
        public override string CommandName => "get_selected_elements";
        public override string Description => "Gets the currently selected elements in Revit";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => Array.Empty<CommandParamSchema>();

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_selected_elements\", \"parameters\": {} }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var uiDoc = app.ActiveUIDocument;
            if (uiDoc is null)
                return CommandResponse.Error(cmd.TaskId, "No active UI document.").ToJson();

            var selectedIds = uiDoc.Selection.GetElementIds();

            if (selectedIds.Count == 0)
            {
                var emptyResult = new
                {
                    count = 0,
                    elements = Array.Empty<object>()
                };
                return CommandResponse.Success(cmd.TaskId, emptyResult, "No elements selected.").ToJson();
            }

            var elements = new List<object>();

            foreach (var id in selectedIds)
            {
                var element = doc.GetElement(id);
                if (element is not null)
                {
                    elements.Add(new
                    {
                        id = id.IntegerValue,
                        name = element.Name ?? string.Empty,
                        category = element.Category?.Name,
                        class_type = element.GetType().Name
                    });
                }
            }

            var result = new
            {
                count = elements.Count,
                elements
            };

            return CommandResponse.Success(cmd.TaskId, result, $"Retrieved {elements.Count} selected elements.").ToJson();
        }
    }
}
