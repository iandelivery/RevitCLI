using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Creation
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
            var p = TryBind<CreateWallParams>(cmd, out var error);
            if (p is null) return error!;

            using (Transaction t = new Transaction(doc, "CLI Create Wall"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                var start = new XYZ(p.StartX.MillimeterToFeet(), p.StartY.MillimeterToFeet(), 0);
                var end = new XYZ(p.EndX.MillimeterToFeet(), p.EndY.MillimeterToFeet(), 0);

                var wall = Wall.Create(
                    doc,
                    Line.CreateBound(start, end),
                    new ElementId(p.LevelId),
                    false);

                t.Commit();

                var result = new
                {
                    element_id = wall.Id.IntegerValue,
                    start_x = p.StartX,
                    start_y = p.StartY,
                    end_x = p.EndX,
                    end_y = p.EndY,
                    level_id = p.LevelId,
                    height = p.Height
                };

                return CommandResponse.Success(cmd.TaskId, result, "Wall created successfully.").ToJson();
            }
        }
    }

    /// <summary>
    /// Typed parameter bag for <see cref="CreateWallHandler"/>.
    /// Replaces six HandlerUtilities.GetXxxOrNull calls plus a manual
    /// null-check error response with a single ParameterBinder.Bind call.
    /// </summary>
    public class CreateWallParams
    {
        [Param("start_x", Required = true)]
        public double StartX { get; set; }

        [Param("start_y", Required = true)]
        public double StartY { get; set; }

        [Param("end_x", Required = true)]
        public double EndX { get; set; }

        [Param("end_y", Required = true)]
        public double EndY { get; set; }

        [Param("level_id", Required = true)]
        public int LevelId { get; set; }

        [Param("height", Default = 3000.0)]
        public double Height { get; set; }
    }
}