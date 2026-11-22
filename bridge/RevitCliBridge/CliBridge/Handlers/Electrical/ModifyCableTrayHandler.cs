using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers;
using RevitCliBridge.Handlers.Mep;

namespace RevitCliBridge.Handlers.Electrical
{
    /// <summary>
    /// Modifies an existing cable tray: endpoints, type, and/or width/height.
    /// At least one optional field must be provided. Coordinates use the
    /// request length unit (default mm); width/height updates take effect
    /// immediately on the instance parameters.
    /// </summary>
    public class ModifyCableTrayHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["x"] = UnitKinds.Length,
                ["y"] = UnitKinds.Length,
                ["z"] = UnitKinds.Length,
                ["width"] = UnitKinds.Length,
                ["height"] = UnitKinds.Length,
                ["length"] = UnitKinds.Length
            };

        public override string CommandName => "modify_cable_tray";
        public override string Description => "Modifies an existing cable tray (endpoints, type, width, height)";
        public override string Category => "Modify";
        public override string[] Aliases => new[] { "cabletray_modify" };
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Cable tray element ID to modify" },
            new CommandParamSchema { Name = "start_x", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New start X (request length unit, default mm)" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New start Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New start Z (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New end X (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New end Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New end Z (request length unit, default mm)" },
            new CommandParamSchema { Name = "type_id", Type = "int", Required = false, Description = "New cable tray type ID" },
            new CommandParamSchema { Name = "width", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New width (request length unit, default mm)" },
            new CommandParamSchema { Name = "height", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New height (request length unit, default mm)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"modify_cable_tray\", \"parameters\": { \"element_id\": 12345, \"end_x\": 6000 } }",
            "{ \"command\": \"modify_cable_tray\", \"parameters\": { \"element_id\": 12345, \"width\": 300, \"height\": 150 } }",
            "{ \"command\": \"modify_cable_tray\", \"parameters\": { \"element_id\": 12345, \"type_id\": 67890 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<ModifyCableTrayParams>(cmd, out var error);
            if (p is null) return error!;

            if (!HasAnyChange(p))
                return CommandResponse.Error(cmd.TaskId, "At least one of start/end coordinates, type_id, width, or height must be provided.").ToJson();

            var tray = doc.GetElement(new ElementId(p.ElementId)) as CableTray;
            if (tray is null)
                return CommandResponse.Error(cmd.TaskId, $"Element {p.ElementId} is not a cable tray.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Modify Cable Tray", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();

                if (p.TypeId.HasValue && p.TypeId.Value > 0 && p.TypeId.Value != tray.GetTypeId().IntegerValue)
                {
                    var newType = doc.GetElement(new ElementId(p.TypeId.Value)) as CableTrayType;
                    if (newType is null)
                        return CommandResponse.Error(cmd.TaskId, $"Element {p.TypeId.Value} is not a cable tray type.").ToJson();
                    tray.ChangeTypeId(newType.Id);
                }

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
                        return CommandResponse.Error(cmd.TaskId, "Cable tray does not have a linear location curve.").ToJson();
                    }
                }

                if (p.Width.HasValue && p.Width.Value > 0)
                    tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.Set(p.Width.Value);

                if (p.Height.HasValue && p.Height.Value > 0)
                    tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.Set(p.Height.Value);

                tx.Commit();

                var result = CableTrayUtils.Default.Snapshot(tray, doc);
                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Cable tray modified successfully.", tx.ResolvedFailures).ToJson();
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
            (p.Width.HasValue && p.Width.Value > 0) ||
            (p.Height.HasValue && p.Height.Value > 0);
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

        [Param("width")]
        public double? Width { get; set; }

        [Param("height")]
        public double? Height { get; set; }
    }
}
