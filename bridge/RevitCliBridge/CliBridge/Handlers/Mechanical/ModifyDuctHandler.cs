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
    /// Modifies an existing duct: endpoints, type, and/or size. At least
    /// one optional field must be provided. Coordinates are in millimeters.
    /// Size parameters are shape-aware: round ducts accept
    /// <c>diameter_mm</c>; rectangular/oval ducts accept <c>width_mm</c>
    /// and <c>height_mm</c>. Setting a shape-mismatched parameter returns
    /// an error.
    /// </summary>
    public class ModifyDuctHandler : DocumentCommandBase
    {
        public override string CommandName => "modify_duct";
        public override string Description => "Modifies an existing duct (endpoints, type, size)";
        public override string Category => "Modify";
        public override string[] Aliases => new[] { "duct_modify" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Duct element ID to modify" },
            new CommandParamSchema { Name = "start_x", Type = "double", Required = false, Description = "New start X in millimeters" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = false, Description = "New start Y in millimeters" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = false, Description = "New start Z in millimeters" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = false, Description = "New end X in millimeters" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = false, Description = "New end Y in millimeters" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = false, Description = "New end Z in millimeters" },
            new CommandParamSchema { Name = "duct_type_id", Type = "int", Required = false, Description = "New duct type ID" },
            new CommandParamSchema { Name = "diameter_mm", Type = "double", Required = false, Description = "New diameter in millimeters (round ducts only)" },
            new CommandParamSchema { Name = "width_mm", Type = "double", Required = false, Description = "New width in millimeters (rectangular/oval only)" },
            new CommandParamSchema { Name = "height_mm", Type = "double", Required = false, Description = "New height in millimeters (rectangular/oval only)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"modify_duct\", \"parameters\": { \"element_id\": 12345, \"end_x\": 6000 } }",
            "{ \"command\": \"modify_duct\", \"parameters\": { \"element_id\": 12345, \"diameter_mm\": 250 } }",
            "{ \"command\": \"modify_duct\", \"parameters\": { \"element_id\": 12345, \"width_mm\": 400, \"height_mm\": 200 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<ModifyDuctParams>(cmd, out var error);
            if (p is null) return error!;

            if (!HasAnyChange(p))
                return CommandResponse.Error(cmd.TaskId, "At least one of start/end coordinates, duct_type_id, diameter_mm, width_mm, or height_mm must be provided.").ToJson();

            var duct = doc.GetElement(new ElementId(p.ElementId)) as Duct;
            if (duct is null)
                return CommandResponse.Error(cmd.TaskId, $"Element {p.ElementId} is not a duct.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Modify Duct", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();

                // Type change
                if (p.DuctTypeId.HasValue && p.DuctTypeId.Value > 0 && p.DuctTypeId.Value != duct.DuctType?.Id.IntegerValue)
                {
                    var newType = doc.GetElement(new ElementId(p.DuctTypeId.Value)) as DuctType;
                    if (newType is null)
                        return CommandResponse.Error(cmd.TaskId, $"Element {p.DuctTypeId.Value} is not a duct type.").ToJson();
                    duct.ChangeTypeId(newType.Id);
                }

                // Endpoint change — must update both together (LocationCurve is a single Line)
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
                            (p.StartX ?? oldStart.X.FeetToMillimeter()).MillimeterToFeet(),
                            (p.StartY ?? oldStart.Y.FeetToMillimeter()).MillimeterToFeet(),
                            (p.StartZ ?? oldStart.Z.FeetToMillimeter()).MillimeterToFeet());

                        var newEnd = new XYZ(
                            (p.EndX ?? oldEnd.X.FeetToMillimeter()).MillimeterToFeet(),
                            (p.EndY ?? oldEnd.Y.FeetToMillimeter()).MillimeterToFeet(),
                            (p.EndZ ?? oldEnd.Z.FeetToMillimeter()).MillimeterToFeet());

                        if (newStart.DistanceTo(newEnd) < DuctUtils.MinSegmentLengthFeet)
                            return CommandResponse.Error(cmd.TaskId, "New endpoints are too close; segment must be at least ~8.5 mm long.").ToJson();

                        locCurve.Curve = Line.CreateBound(newStart, newEnd);
                    }
                    else
                    {
                        return CommandResponse.Error(cmd.TaskId, "Duct does not have a linear location curve.").ToJson();
                    }
                }

                // Size changes — shape-aware.
                var shape = duct.DuctType?.Shape ?? ConnectorProfileType.Round;
                if (shape == ConnectorProfileType.Round)
                {
                    if (p.DiameterMm.HasValue && p.DiameterMm.Value > 0)
                        duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.Set(p.DiameterMm.Value.MillimeterToFeet());

                    // Reject width/height on round ducts.
                    if ((p.WidthMm.HasValue && p.WidthMm.Value > 0) || (p.HeightMm.HasValue && p.HeightMm.Value > 0))
                        return CommandResponse.Error(cmd.TaskId, "Cannot set width_mm/height_mm on a round duct; use diameter_mm instead.").ToJson();
                }
                else
                {
                    if (p.WidthMm.HasValue && p.WidthMm.Value > 0)
                        duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.Set(p.WidthMm.Value.MillimeterToFeet());
                    if (p.HeightMm.HasValue && p.HeightMm.Value > 0)
                        duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.Set(p.HeightMm.Value.MillimeterToFeet());

                    // Reject diameter on rectangular/oval ducts.
                    if (p.DiameterMm.HasValue && p.DiameterMm.Value > 0)
                        return CommandResponse.Error(cmd.TaskId, "Cannot set diameter_mm on a rectangular/oval duct; use width_mm/height_mm instead.").ToJson();
                }

                tx.Commit();

                var result = DuctUtils.Snapshot(duct, doc);
                return CommandResponse.Success(cmd.TaskId, result, "Duct modified successfully.").ToJson();
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
            (p.DiameterMm.HasValue && p.DiameterMm.Value > 0) ||
            (p.WidthMm.HasValue && p.WidthMm.Value > 0) ||
            (p.HeightMm.HasValue && p.HeightMm.Value > 0);
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

        [Param("diameter_mm")]
        public double? DiameterMm { get; set; }

        [Param("width_mm")]
        public double? WidthMm { get; set; }

        [Param("height_mm")]
        public double? HeightMm { get; set; }
    }
}
