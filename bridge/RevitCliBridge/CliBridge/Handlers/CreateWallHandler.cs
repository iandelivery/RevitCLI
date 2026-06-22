using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class CreateWallHandler : DocumentCommandBase
    {
        public override string CommandName => "create_wall";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            double? startX = HandlerUtilities.GetDoubleOrNull(parameters, "start_x");
            double? startY = HandlerUtilities.GetDoubleOrNull(parameters, "start_y");
            double? endX = HandlerUtilities.GetDoubleOrNull(parameters, "end_x");
            double? endY = HandlerUtilities.GetDoubleOrNull(parameters, "end_y");
            double? height = HandlerUtilities.GetDoubleOrNull(parameters, "height");
            int? level_id = HandlerUtilities.GetIntOrNull(parameters, "level_id");

            if (startX is null || startY is null || endX is null || endY is null || level_id is null)
                return CommandResponse.Error(cmd.TaskId,
                    "Missing required parameters: start_x, start_y, end_x, end_y, level_id.").ToJson();

            using (Transaction t = new Transaction(doc, "CLI Create Wall"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                var start = new XYZ(startX.Value.MillimeterToFeet(), startY.Value.MillimeterToFeet(), 0);
                var end = new XYZ(endX.Value.MillimeterToFeet(), endY.Value.MillimeterToFeet(), 0);

                var wall = Wall.Create(
                    doc,
                    Line.CreateBound(start, end),
                    new ElementId(level_id.Value),
                    false);

                t.Commit();

                var result = new
                {
                    element_id = wall.Id.IntegerValue,
                    start_x = startX,
                    start_y = startY,
                    end_x = endX,
                    end_y = endY,
                    level_id = level_id,
                    height = height ?? 3000.0
                };

                return CommandResponse.Success(cmd.TaskId, result, "Wall created successfully.").ToJson();
            }
        }
    }
}