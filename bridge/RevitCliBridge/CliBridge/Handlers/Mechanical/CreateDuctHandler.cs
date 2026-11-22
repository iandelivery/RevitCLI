using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers;
using RevitCliBridge.Handlers.Mep;

namespace RevitCliBridge.Handlers.Mechanical
{
    /// <summary>
    /// Creates a straight duct segment between two 3D points. Coordinates
    /// use the request length unit (default mm). Ducts require a system
    /// type (e.g. SupplyAir) at creation time — use
    /// <c>get_duct_system_types</c> to discover available IDs. Size
    /// parameters depend on the duct type's shape: round uses
    /// <c>diameter</c>; rectangular/oval use <c>width</c> and
    /// <c>height</c>.
    /// </summary>
    public class CreateDuctHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["x"] = UnitKinds.Length,
                ["y"] = UnitKinds.Length,
                ["z"] = UnitKinds.Length,
                ["diameter"] = UnitKinds.Length,
                ["width"] = UnitKinds.Length,
                ["height"] = UnitKinds.Length,
                ["length"] = UnitKinds.Length
            };

        public override string CommandName => "create_duct";
        public override string Description => "Creates a straight duct segment between two 3D points";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "duct_create" };
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "start_x", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "Start X (request length unit, default mm)" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "Start Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "Start Z (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "End X (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "End Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "End Z (request length unit, default mm)" },
            new CommandParamSchema { Name = "level_id", Type = "int", Required = true, Description = "Level element ID to place the duct on" },
            new CommandParamSchema { Name = "system_type_id", Type = "int", Required = true, Description = "Mechanical system type ID (use get_duct_system_types to discover)" },
            new CommandParamSchema { Name = "duct_type_id", Type = "int", Required = false, Description = "Duct type ID (defaults to first available)" },
            new CommandParamSchema { Name = "diameter", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Diameter (request length unit, default mm; round ducts only)" },
            new CommandParamSchema { Name = "width", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Width (request length unit, default mm; rectangular/oval only)" },
            new CommandParamSchema { Name = "height", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Height (request length unit, default mm; rectangular/oval only)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_duct\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 5000, \"end_y\": 0, \"end_z\": 3000, \"level_id\": 3001, \"system_type_id\": 12345 } }",
            "{ \"command\": \"create_duct\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 5000, \"end_y\": 0, \"end_z\": 3000, \"level_id\": 3001, \"system_type_id\": 12345, \"diameter\": 200 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<CreateDuctParams>(cmd, out var error);
            if (p is null) return error!;

            var ductType = DuctUtils.Default.ResolveType(doc, p.DuctTypeId);
            if (ductType is null)
                return CommandResponse.Error(cmd.TaskId, "No duct type found in the document.").ToJson();

            var systemType = DuctUtils.Default.ResolveSystemType(doc, p.SystemTypeId);
            if (systemType is null)
                return CommandResponse.Error(cmd.TaskId, $"Element {p.SystemTypeId} is not a valid mechanical system type. Use get_duct_system_types to discover available IDs.").ToJson();

            var start = new XYZ(p.StartX, p.StartY, p.StartZ);
            var end = new XYZ(p.EndX, p.EndY, p.EndZ);

            if (start.DistanceTo(end) < MepCurveGeometry.MinSegmentLengthFeet)
                return CommandResponse.Error(cmd.TaskId, "Start and end points are too close; segment must be at least ~8.5 mm long.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Duct", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var duct = Duct.Create(doc, systemType.Id, ductType.Id, new ElementId(p.LevelId), start, end);

                var shape = ductType.Shape;
                if (shape == ConnectorProfileType.Round && p.Diameter > 0)
                {
                    duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.Set(p.Diameter);
                }
                else if (shape != ConnectorProfileType.Round)
                {
                    if (p.Width > 0)
                        duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.Set(p.Width);
                    if (p.Height > 0)
                        duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.Set(p.Height);
                }

                tx.Commit();

                var result = DuctUtils.Default.Snapshot(duct, doc);
                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Duct created successfully.", tx.ResolvedFailures).ToJson();
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

        [Param("diameter")]
        public double Diameter { get; set; }

        [Param("width")]
        public double Width { get; set; }

        [Param("height")]
        public double Height { get; set; }
    }
}
