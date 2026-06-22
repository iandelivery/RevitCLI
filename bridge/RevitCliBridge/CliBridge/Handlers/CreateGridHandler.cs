using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class CreateGridHandler : DocumentCommandBase
    {
        public override string CommandName => "create_grid";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            double? startX = HandlerUtilities.GetDoubleOrNull(parameters, "start_x");
            double? startY = HandlerUtilities.GetDoubleOrNull(parameters, "start_y");
            double? endX = HandlerUtilities.GetDoubleOrNull(parameters, "end_x");
            double? endY = HandlerUtilities.GetDoubleOrNull(parameters, "end_y");
            string? gridName = HandlerUtilities.GetStringOrNull(parameters, "name");

            if (startX is null || startY is null || endX is null || endY is null)
                return CommandResponse.Error(cmd.TaskId,
                    "Missing required parameters: start_x, start_y, end_x, end_y.").ToJson();

            if (string.IsNullOrWhiteSpace(gridName))
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: name.").ToJson();

            using (Transaction t = new Transaction(doc, "CLI Create Grid"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                var start = new XYZ(startX.Value.MillimeterToFeet(), startY.Value.MillimeterToFeet(), 0);
                var end = new XYZ(endX.Value.MillimeterToFeet(), endY.Value.MillimeterToFeet(), 0);

                var line = Line.CreateBound(start, end);
                var grid = Grid.Create(doc, line);
                grid.Name = gridName;

                t.Commit();

                var result = new
                {
                    element_id = grid.Id.IntegerValue,
                    name = grid.Name,
                    start_x = startX,
                    start_y = startY,
                    end_x = endX,
                    end_y = endY
                };

                return CommandResponse.Success(cmd.TaskId, result, "Grid created successfully.").ToJson();
            }
        }
    }
}
