using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers;

namespace RevitCliBridge.Handlers.Electrical
{
    /// <summary>
    /// Creates a straight cable tray segment between two 3D points.
    /// Coordinates are accepted in millimeters and converted to internal
    /// feet. Width and height are optional — when omitted, the type's
    /// default dimensions are used.
    /// </summary>
    public class CreateCableTrayHandler : DocumentCommandBase
    {
        public override string CommandName => "create_cable_tray";
        public override string Description => "Creates a straight cable tray segment between two 3D points";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "cabletray_create" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "start_x", Type = "double", Required = true, Description = "Start X in millimeters" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = true, Description = "Start Y in millimeters" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = true, Description = "Start Z in millimeters" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = true, Description = "End X in millimeters" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = true, Description = "End Y in millimeters" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = true, Description = "End Z in millimeters" },
            new CommandParamSchema { Name = "level_id", Type = "int", Required = true, Description = "Level element ID to place the tray on" },
            new CommandParamSchema { Name = "type_id", Type = "int", Required = false, Description = "Cable tray type ID (defaults to first available)" },
            new CommandParamSchema { Name = "width_mm", Type = "double", Required = false, Description = "Width in millimeters (defaults to type default)" },
            new CommandParamSchema { Name = "height_mm", Type = "double", Required = false, Description = "Height in millimeters (defaults to type default)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_cable_tray\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 5000, \"end_y\": 0, \"end_z\": 3000, \"level_id\": 3001 } }",
            "{ \"command\": \"create_cable_tray\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 5000, \"end_y\": 0, \"end_z\": 3000, \"level_id\": 3001, \"type_id\": 12345, \"width_mm\": 200, \"height_mm\": 100 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<CreateCableTrayParams>(cmd, out var error);
            if (p is null) return error!;

            var trayType = CableTrayUtils.ResolveType(doc, p.TypeId);
            if (trayType is null)
                return CommandResponse.Error(cmd.TaskId, "No cable tray type found in the document.").ToJson();

            var start = new XYZ(p.StartX.MillimeterToFeet(), p.StartY.MillimeterToFeet(), p.StartZ.MillimeterToFeet());
            var end = new XYZ(p.EndX.MillimeterToFeet(), p.EndY.MillimeterToFeet(), p.EndZ.MillimeterToFeet());

            if (start.DistanceTo(end) < CableTrayUtils.MinSegmentLengthFeet)
                return CommandResponse.Error(cmd.TaskId, "Start and end points are too close; segment must be at least ~8.5 mm long.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Cable Tray", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var tray = CableTray.Create(doc, trayType.Id, start, end, new ElementId(p.LevelId));

                if (p.WidthMm > 0)
                {
                    var w = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
                    w?.Set(p.WidthMm.MillimeterToFeet());
                }
                if (p.HeightMm > 0)
                {
                    var h = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
                    h?.Set(p.HeightMm.MillimeterToFeet());
                }

                tx.Commit();

                var result = CableTrayUtils.Snapshot(tray, doc);
                return CommandResponse.Success(cmd.TaskId, result, "Cable tray created successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the creation: {ex.Message}").ToJson();
            }
        }
    }

    public class CreateCableTrayParams
    {
        [Param("start_x", Required = true)]
        public double StartX { get; set; }

        [Param("start_y", Required = true)]
        public double StartY { get; set; }

        [Param("start_z", Required = true)]
        public double StartZ { get; set; }

        [Param("end_x", Required = true)]
        public double EndX { get; set; }

        [Param("end_y", Required = true)]
        public double EndY { get; set; }

        [Param("end_z", Required = true)]
        public double EndZ { get; set; }

        [Param("level_id", Required = true)]
        public int LevelId { get; set; }

        [Param("type_id")]
        public int? TypeId { get; set; }

        [Param("width_mm")]
        public double WidthMm { get; set; }

        [Param("height_mm")]
        public double HeightMm { get; set; }
    }
}
