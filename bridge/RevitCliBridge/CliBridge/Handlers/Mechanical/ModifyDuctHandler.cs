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
    /// Modifies an existing duct: endpoints, type, and/or size. At least
    /// one optional field must be provided. Coordinates use the request
    /// length unit (default mm). Size parameters are shape-aware: round
    /// ducts accept <c>diameter</c>; rectangular/oval ducts accept
    /// <c>width</c> and <c>height</c>. Setting a shape-mismatched parameter
    /// returns an error.
    /// </summary>
    public class ModifyDuctHandler : DocumentCommandBase
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

        public override string CommandName => "modify_duct";
        public override string Description => "Modifies an existing duct (endpoints, type, size)";
        public override string Category => "Modify";
        public override string[] Aliases => new[] { "duct_modify" };
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Duct element ID to modify" },
            new CommandParamSchema { Name = "start_x", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New start X (request length unit, default mm)" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New start Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New start Z (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New end X (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New end Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New end Z (request length unit, default mm)" },
            new CommandParamSchema { Name = "duct_type_id", Type = "int", Required = false, Description = "New duct type ID" },
            new CommandParamSchema { Name = "diameter", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New diameter (request length unit, default mm; round ducts only)" },
            new CommandParamSchema { Name = "width", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New width (request length unit, default mm; rectangular/oval only)" },
            new CommandParamSchema { Name = "height", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New height (request length unit, default mm; rectangular/oval only)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"modify_duct\", \"parameters\": { \"element_id\": 12345, \"end_x\": 6000 } }",
            "{ \"command\": \"modify_duct\", \"parameters\": { \"element_id\": 12345, \"diameter\": 250 } }",
            "{ \"command\": \"modify_duct\", \"parameters\": { \"element_id\": 12345, \"width\": 400, \"height\": 200 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<ModifyDuctParams>(cmd, out var error);
            if (p is null) return error!;

            if (!HasAnyChange(p))
                return CommandResponse.Error(cmd.TaskId, "At least one of start/end coordinates, duct_type_id, diameter, width, or height must be provided.").ToJson();

            var duct = doc.GetElement(ElementIdCompat.FromValue(p.ElementId)) as Duct;
            if (duct is null)
                return CommandResponse.Error(cmd.TaskId, $"Element {p.ElementId} is not a duct.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Modify Duct", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();

                if (p.DuctTypeId.HasValue && p.DuctTypeId.Value > 0 && p.DuctTypeId.Value != ElementIdCompat.ValueOf(duct.DuctType?.Id))
                {
                    var newType = doc.GetElement(ElementIdCompat.FromValue(p.DuctTypeId.Value)) as DuctType;
                    if (newType is null)
                        return CommandResponse.Error(cmd.TaskId, $"Element {p.DuctTypeId.Value} is not a duct type.").ToJson();
                    duct.ChangeTypeId(newType.Id);
                }

                bool hasStart = p.StartX.HasValue || p.StartY.HasValue || p.StartZ.HasValue;
                bool hasEnd = p.EndX.HasValue || p.EndY.HasValue || p.EndZ.HasValue;
                if (hasStart || hasEnd)
                {
                    var locCurve = duct.Location as LocationCurve;
                    if (locCurve?.Curve is Line line)
                    {
                        var oldStart = line.GetEndPoint(0);
                        var oldEnd = line.GetEndPoint(1);

                        var newStart = new XYZ(
                            p.StartX ?? oldStart.X,
                            p.StartY ?? oldStart.Y,
                            p.StartZ ?? oldStart.Z);

                        var newEnd = new XYZ(
                            p.EndX ?? oldEnd.X,
                            p.EndY ?? oldEnd.Y,
                            p.EndZ ?? oldEnd.Z);

                        if (newStart.DistanceTo(newEnd) < MepCurveGeometry.MinSegmentLengthFeet)
                            return CommandResponse.Error(cmd.TaskId, "New endpoints are too close; segment must be at least ~8.5 mm long.").ToJson();

                        locCurve.Curve = Line.CreateBound(newStart, newEnd);
                    }
                    else
                    {
                        return CommandResponse.Error(cmd.TaskId, "Duct does not have a linear location curve.").ToJson();
                    }
                }

                var shape = duct.DuctType?.Shape ?? ConnectorProfileType.Round;
                if (shape == ConnectorProfileType.Round)
                {
                    if (p.Diameter.HasValue && p.Diameter.Value > 0)
                        duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.Set(p.Diameter.Value);

                    if ((p.Width.HasValue && p.Width.Value > 0) || (p.Height.HasValue && p.Height.Value > 0))
                        return CommandResponse.Error(cmd.TaskId, "Cannot set width/height on a round duct; use diameter instead.").ToJson();
                }
                else
                {
                    if (p.Width.HasValue && p.Width.Value > 0)
                        duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.Set(p.Width.Value);
                    if (p.Height.HasValue && p.Height.Value > 0)
                        duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.Set(p.Height.Value);

                    if (p.Diameter.HasValue && p.Diameter.Value > 0)
                        return CommandResponse.Error(cmd.TaskId, "Cannot set diameter on a rectangular/oval duct; use width/height instead.").ToJson();
                }

                tx.Commit();

                var result = DuctUtils.Default.Snapshot(duct, doc);
                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Duct modified successfully.", tx.ResolvedFailures).ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the modification: {ex.Message}").ToJson();
            }
        }

        private static bool HasAnyChange(ModifyDuctParams p) =>
            p.StartX.HasValue || p.StartY.HasValue || p.StartZ.HasValue ||
            p.EndX.HasValue || p.EndY.HasValue || p.EndZ.HasValue ||
            (p.DuctTypeId.HasValue && p.DuctTypeId.Value > 0) ||
            (p.Diameter.HasValue && p.Diameter.Value > 0) ||
            (p.Width.HasValue && p.Width.Value > 0) ||
            (p.Height.HasValue && p.Height.Value > 0);
    }

    public class ModifyDuctParams
    {
        [Param("element_id", Required = true)]
        public int ElementId { get; set; }

        [Param("start_x")]
        public double? StartX { get; set; }

        [Param("start_y")]
        public double? StartY { get; set; }

        [Param("start_z")]
        public double? StartZ { get; set; }

        [Param("end_x")]
        public double? EndX { get; set; }

        [Param("end_y")]
        public double? EndY { get; set; }

        [Param("end_z")]
        public double? EndZ { get; set; }

        [Param("duct_type_id")]
        public int? DuctTypeId { get; set; }

        [Param("diameter")]
        public double? Diameter { get; set; }

        [Param("width")]
        public double? Width { get; set; }

        [Param("height")]
        public double? Height { get; set; }
    }
}
