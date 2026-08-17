using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers;

namespace RevitCliBridge.Handlers.Mechanical
{
    /// <summary>
    /// Creates a straight duct segment between two 3D points. Coordinates
    /// are accepted in millimeters and converted to internal feet. Ducts
    /// require a system type (e.g. SupplyAir) at creation time — use
    /// <c>get_duct_system_types</c> to discover available IDs. Size
    /// parameters depend on the duct type's shape: round uses
    /// <c>diameter_mm</c>; rectangular/oval use <c>width_mm</c> and
    /// <c>height_mm</c>.
    /// </summary>
    public class CreateDuctHandler : DocumentCommandBase
    {
        public override string CommandName => "create_duct";
        public override string Description => "Creates a straight duct segment between two 3D points";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "duct_create" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "start_x", Type = "double", Required = true, Description = "Start X in millimeters" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = true, Description = "Start Y in millimeters" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = true, Description = "Start Z in millimeters" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = true, Description = "End X in millimeters" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = true, Description = "End Y in millimeters" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = true, Description = "End Z in millimeters" },
            new CommandParamSchema { Name = "level_id", Type = "int", Required = true, Description = "Level element ID to place the duct on" },
            new CommandParamSchema { Name = "system_type_id", Type = "int", Required = true, Description = "Mechanical system type ID (use get_duct_system_types to discover)" },
            new CommandParamSchema { Name = "duct_type_id", Type = "int", Required = false, Description = "Duct type ID (defaults to first available)" },
            new CommandParamSchema { Name = "diameter_mm", Type = "double", Required = false, Description = "Diameter in millimeters (round ducts only)" },
            new CommandParamSchema { Name = "width_mm", Type = "double", Required = false, Description = "Width in millimeters (rectangular/oval only)" },
            new CommandParamSchema { Name = "height_mm", Type = "double", Required = false, Description = "Height in millimeters (rectangular/oval only)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_duct\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 5000, \"end_y\": 0, \"end_z\": 3000, \"level_id\": 3001, \"system_type_id\": 12345 } }",
            "{ \"command\": \"create_duct\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 5000, \"end_y\": 0, \"end_z\": 3000, \"level_id\": 3001, \"system_type_id\": 12345, \"diameter_mm\": 200 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<CreateDuctParams>(cmd, out var error);
            if (p is null) return error!;

            var ductType = DuctUtils.ResolveType(doc, p.DuctTypeId);
            if (ductType is null)
                return CommandResponse.Error(cmd.TaskId, "No duct type found in the document.").ToJson();

            var systemType = DuctUtils.ResolveSystemType(doc, p.SystemTypeId);
            if (systemType is null)
                return CommandResponse.Error(cmd.TaskId, $"Element {p.SystemTypeId} is not a valid mechanical system type. Use get_duct_system_types to discover available IDs.").ToJson();

            var start = new XYZ(p.StartX.MillimeterToFeet(), p.StartY.MillimeterToFeet(), p.StartZ.MillimeterToFeet());
            var end = new XYZ(p.EndX.MillimeterToFeet(), p.EndY.MillimeterToFeet(), p.EndZ.MillimeterToFeet());

            if (start.DistanceTo(end) < DuctUtils.MinSegmentLengthFeet)
                return CommandResponse.Error(cmd.TaskId, "Start and end points are too close; segment must be at least ~8.5 mm long.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Duct", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var duct = Duct.Create(doc, systemType.Id, ductType.Id, new ElementId(p.LevelId), start, end);

                // Apply size based on shape.
                var shape = ductType.Shape;
                if (shape == ConnectorProfileType.Round && p.DiameterMm > 0)
                {
                    duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.Set(p.DiameterMm.MillimeterToFeet());
                }
                else if (shape != ConnectorProfileType.Round)
                {
                    if (p.WidthMm > 0)
                        duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.Set(p.WidthMm.MillimeterToFeet());
                    if (p.HeightMm > 0)
                        duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.Set(p.HeightMm.MillimeterToFeet());
                }

                tx.Commit();

                var result = DuctUtils.Snapshot(duct, doc);
                return CommandResponse.Success(cmd.TaskId, result, "Duct created successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the creation: {ex.Message}").ToJson();
            }
        }
    }

    public class CreateDuctParams
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

        [Param("system_type_id", Required = true)]
        public int SystemTypeId { get; set; }

        [Param("duct_type_id")]
        public int? DuctTypeId { get; set; }

        [Param("diameter_mm")]
        public double DiameterMm { get; set; }

        [Param("width_mm")]
        public double WidthMm { get; set; }

        [Param("height_mm")]
        public double HeightMm { get; set; }
    }
}
