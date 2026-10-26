using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Query
{
    public class GetElementBoxesHandler : DocumentCommandBase
    {
        public override string CommandName => "get_element_boxes";
        public override string Description => "Returns bounding boxes for elements in millimeters, in model space or in a view's coordinate system (clipped by an active section box)";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_ids", Type = "int[]", Required = true, Description = "Element IDs to get bounding boxes for" },
            new CommandParamSchema { Name = "view_id", Type = "int", Required = false, Description = "View element ID (returns view-space boxes clipped by the section box; defaults to model space)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_element_boxes\", \"parameters\": { \"element_ids\": [123, 456] } }",
            "{ \"command\": \"get_element_boxes\", \"parameters\": { \"element_ids\": [123], \"view_id\": 789 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var elementIds = HandlerUtilities.GetIntArrayOrNull(parameters, "element_ids");
            if (elementIds is null || elementIds.Length == 0)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: element_ids.").ToJson();

            Autodesk.Revit.DB.View? view = null;
            int? viewId = HandlerUtilities.GetIntOrNull(parameters, "view_id");
            if (viewId.HasValue)
            {
                view = doc.GetElement(new ElementId(viewId.Value)) as Autodesk.Revit.DB.View;
                if (view is null)
                    return CommandResponse.Error(cmd.TaskId, $"Element with ID {viewId.Value} is not a view.").ToJson();
            }

            string space = view is null ? "model" : "view";
            var elements = new List<object>();

            foreach (var id in elementIds.Distinct())
            {
                var element = doc.GetElement(new ElementId(id));
                var box = element?.get_BoundingBox(view);

                if (box is null)
                {
                    elements.Add(new { id, found = false });
                    continue;
                }

                elements.Add(new
                {
                    id,
                    found = true,
                    min_x = box.Min.X.FeetToMillimeter(),
                    min_y = box.Min.Y.FeetToMillimeter(),
                    min_z = box.Min.Z.FeetToMillimeter(),
                    max_x = box.Max.X.FeetToMillimeter(),
                    max_y = box.Max.Y.FeetToMillimeter(),
                    max_z = box.Max.Z.FeetToMillimeter()
                });
            }

            var result = new
            {
                count = elements.Count,
                space,
                view_id = viewId,
                elements
            };

            return CommandResponse.Success(cmd.TaskId, result, "Element bounding boxes resolved.").ToJson();
        }
    }
}
