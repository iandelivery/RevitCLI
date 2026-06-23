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
        public override string Description => "Creates a new wall between two points on a specified level";
        public override string Category => "Create";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "start_x", Type = "double", Required = true, Description = "Start X coordinate in millimeters" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = true, Description = "Start Y coordinate in millimeters" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = true, Description = "End X coordinate in millimeters" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = true, Description = "End Y coordinate in millimeters" },
            new CommandParamSchema { Name = "level_id", Type = "int", Required = true, Description = "Level element ID to place the wall on" },
            new CommandParamSchema { Name = "height", Type = "double", Required = false, Description = "Wall height in millimeters (optional)", Default = 3000 }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_wall\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"end_x\": 5000, \"end_y\": 0, \"level_id\": 3001 } }",
            "{ \"command\": \"create_wall\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"end_x\": 0, \"end_y\": 4000, \"level_id\": 3001, \"height\": 2800 } }"
        };

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