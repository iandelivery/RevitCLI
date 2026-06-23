using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class ZoomToFitHandler : DocumentCommandBase
    {
        public override string CommandName => "zoom_to_fit";
        public override string Description => "Zooms the active view to fit all elements";
        public override string Category => "UI";

        public override CommandParamSchema[] Parameters => Array.Empty<CommandParamSchema>();

        public override string[] Examples => new[]
        {
            "{ \"command\": \"zoom_to_fit\", \"parameters\": {} }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var uiDoc = app.ActiveUIDocument;
            if (uiDoc is null)
                return CommandResponse.Error(cmd.TaskId, "No active document.").ToJson();

            int? elementId = HandlerUtilities.GetIntOrNull(parameters, "element_id");
            var elementIds = HandlerUtilities.GetIntArrayOrNull(parameters, "element_ids");

            if (elementId is null && (elementIds is null || elementIds.Length == 0))
            {
                // If no element specified, zoom to fit all model elements
                uiDoc.ShowElements(new List<ElementId>());
                var result = new { zoom_type = "all" };
                return CommandResponse.Success(cmd.TaskId, result, "Zoomed to fit all elements.").ToJson();
            }

            var idsToShow = new List<ElementId>();

            if (elementId is not null)
            {
                var element = doc.GetElement(new ElementId(elementId.Value));
                if (element is null)
                    return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId.Value} not found.").ToJson();

                idsToShow.Add(element.Id);
            }
            else if (elementIds is not null)
            {
                foreach (var id in elementIds)
                {
                    var element = doc.GetElement(new ElementId(id));
                    if (element is not null)
                        idsToShow.Add(element.Id);
                }

                if (idsToShow.Count == 0)
                    return CommandResponse.Error(cmd.TaskId, "No valid elements found.").ToJson();
            }

            uiDoc.ShowElements(idsToShow);

            var resultWithElements = new
            {
                zoom_type = "elements",
                element_count = idsToShow.Count,
                element_ids = idsToShow.Select(id => id.IntegerValue).ToArray()
            };

            return CommandResponse.Success(cmd.TaskId, resultWithElements, "Zoomed to fit elements.").ToJson();
        }
    }
}
