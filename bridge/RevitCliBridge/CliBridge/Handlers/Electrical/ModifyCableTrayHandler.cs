using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers;

namespace RevitCliBridge.Handlers.Electrical
{
    /// <summary>
    /// Modifies an existing cable tray: endpoints, type, and/or width/height.
    /// At least one optional field must be provided. Coordinates are in
    /// millimeters; width/height updates take effect immediately on the
    /// instance parameters.
    /// </summary>
    public class ModifyCableTrayHandler : DocumentCommandBase
    {
        public override string CommandName => "modify_cable_tray";
        public override string Description => "Modifies an existing cable tray (endpoints, type, width, height)";
        public override string Category => "Modify";
        public override string[] Aliases => new[] { "cabletray_modify" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Cable tray element ID to modify" },
            new CommandParamSchema { Name = "start_x", Type = "double", Required = false, Description = "New start X in millimeters" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = false, Description = "New start Y in millimeters" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = false, Description = "New start Z in millimeters" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = false, Description = "New end X in millimeters" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = false, Description = "New end Y in millimeters" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = false, Description = "New end Z in millimeters" },
            new CommandParamSchema { Name = "type_id", Type = "int", Required = false, Description = "New cable tray type ID" },
            new CommandParamSchema { Name = "width_mm", Type = "double", Required = false, Description = "New width in millimeters" },
            new CommandParamSchema { Name = "height_mm", Type = "double", Required = false, Description = "New height in millimeters" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"modify_cable_tray\", \"parameters\": { \"element_id\": 12345, \"end_x\": 6000 } }",
            "{ \"command\": \"modify_cable_tray\", \"parameters\": { \"element_id\": 12345, \"width_mm\": 300, \"height_mm\": 150 } }",
            "{ \"command\": \"modify_cable_tray\", \"parameters\": { \"element_id\": 12345, \"type_id\": 67890 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<ModifyCableTrayParams>(cmd, out var error);
            if (p is null) return error!;

            if (!HasAnyChange(p))
                return CommandResponse.Error(cmd.TaskId, "At least one of start/end coordinates, type_id, width_mm, or height_mm must be provided.").ToJson();

            var tray = doc.GetElement(new ElementId(p.ElementId)) as CableTray;
            if (tray is null)
                return CommandResponse.Error(cmd.TaskId, $"Element {p.ElementId} is not a cable tray.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Modify Cable Tray", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();

                // Type change
                if (p.TypeId.HasValue && p.TypeId.Value > 0 && p.TypeId.Value != tray.GetTypeId().IntegerValue)
                {
                    var newType = doc.GetElement(new ElementId(p.TypeId.Value)) as CableTrayType;
                    if (newType is null)
                        return CommandResponse.Error(cmd.TaskId, $"Element {p.TypeId.Value} is not a cable tray type.").ToJson();
                    tray.ChangeTypeId(newType.Id);
                }

                // Endpoint change — must update both together (LocationCurve is a single Line)
                bool hasStart = p.StartX.HasValue || p.StartY.HasValue || p.StartZ.HasValue;
                bool hasEnd = p.EndX.HasValue || p.EndY.HasValue || p.EndZ.HasValue;
                if (hasStart || hasEnd)
                {
                    var locCurve = tray.Location as LocationCurve;
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

                        if (newStart.DistanceTo(newEnd) < CableTrayUtils.MinSegmentLengthFeet)
                            return CommandResponse.Error(cmd.TaskId, "New endpoints are too close; segment must be at least ~8.5 mm long.").ToJson();

                        locCurve.Curve = Line.CreateBound(newStart, newEnd);
                    }
                    else
                    {
                        return CommandResponse.Error(cmd.TaskId, "Cable tray does not have a linear location curve.").ToJson();
                    }
                }

                // Width / height
                if (p.WidthMm.HasValue && p.WidthMm.Value > 0)
                    tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.Set(p.WidthMm.Value.MillimeterToFeet());

                if (p.HeightMm.HasValue && p.HeightMm.Value > 0)
                    tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.Set(p.HeightMm.Value.MillimeterToFeet());

                tx.Commit();

                var result = CableTrayUtils.Snapshot(tray, doc);
                return CommandResponse.Success(cmd.TaskId, result, "Cable tray modified successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the modification: {ex.Message}").ToJson();
            }
        }

        private static bool HasAnyChange(ModifyCableTrayParams p) =>
            p.StartX.HasValue || p.StartY.HasValue || p.StartZ.HasValue ||
            p.EndX.HasValue || p.EndY.HasValue || p.EndZ.HasValue ||
            (p.TypeId.HasValue && p.TypeId.Value > 0) ||
            (p.WidthMm.HasValue && p.WidthMm.Value > 0) ||
            (p.HeightMm.HasValue && p.HeightMm.Value > 0);
    }

    public class ModifyCableTrayParams
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

        [Param("type_id")]
        public int? TypeId { get; set; }

        [Param("width_mm")]
        public double? WidthMm { get; set; }

        [Param("height_mm")]
        public double? HeightMm { get; set; }
    }
}
