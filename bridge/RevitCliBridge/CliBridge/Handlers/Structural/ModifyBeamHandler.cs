using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Structural
{
    /// <summary>
    /// Modifies an existing beam: endpoints and/or family type. At least one
    /// optional field must be provided. Coordinates are in millimeters;
    /// omitted components keep their current value. Braces are rejected —
    /// only elements with StructuralType.Beam are accepted.
    /// </summary>
    public class ModifyBeamHandler : DocumentCommandBase
    {
        public override string CommandName => "modify_beam";
        public override string Description => "Modifies an existing beam (endpoints, family type)";
        public override string Category => "Modify";
        public override string[] Aliases => new[] { "beam_modify" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Beam element ID to modify" },
            new CommandParamSchema { Name = "start_x", Type = "double", Required = false, Description = "New start X in millimeters" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = false, Description = "New start Y in millimeters" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = false, Description = "New start Z in millimeters" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = false, Description = "New end X in millimeters" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = false, Description = "New end Y in millimeters" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = false, Description = "New end Z in millimeters" },
            new CommandParamSchema { Name = "symbol_id", Type = "int", Required = false, Description = "New beam FamilySymbol ID (list via get_family_symbols --category OST_StructuralFraming)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"modify_beam\", \"parameters\": { \"element_id\": 12345, \"end_x\": 6000 } }",
            "{ \"command\": \"modify_beam\", \"parameters\": { \"element_id\": 12345, \"start_z\": 3300, \"end_z\": 3300, \"symbol_id\": 3417 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<ModifyBeamParams>(cmd, out var error);
            if (p is null) return error!;

            if (!HasAnyChange(p))
                return CommandResponse.Error(cmd.TaskId, "At least one of start/end coordinates or symbol_id must be provided.").ToJson();

            var beam = doc.GetElement(new ElementId(p.ElementId)) as FamilyInstance;
            if (beam is null || !StructuralUtils.Default.IsBeam(beam))
                return CommandResponse.Error(cmd.TaskId, $"Element {p.ElementId} is not a beam.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Modify Beam", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();

                // Type change
                if (p.SymbolId.HasValue && p.SymbolId.Value > 0 && p.SymbolId.Value != beam.GetTypeId().IntegerValue)
                {
                    var newSymbol = StructuralUtils.Default.ResolveSymbol(doc, p.SymbolId, BuiltInCategory.OST_StructuralFraming);
                    if (newSymbol is null)
                        return CommandResponse.Error(cmd.TaskId,
                            $"FamilySymbol with ID {p.SymbolId.Value} not found or is not a structural framing type.").ToJson();
                    beam.ChangeTypeId(newSymbol.Id);
                }

                // Endpoint change — must update both together (LocationCurve is a single Line)
                bool hasStart = p.StartX.HasValue || p.StartY.HasValue || p.StartZ.HasValue;
                bool hasEnd = p.EndX.HasValue || p.EndY.HasValue || p.EndZ.HasValue;
                if (hasStart || hasEnd)
                {
                    var locCurve = beam.Location as LocationCurve;
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

                        if (newStart.DistanceTo(newEnd) < StructuralUtils.MinSegmentLengthFeet)
                            return CommandResponse.Error(cmd.TaskId,
                                "New endpoints are too close; the beam segment must be longer than ~2.5 mm.").ToJson();

                        locCurve.Curve = Line.CreateBound(newStart, newEnd);
                    }
                    else
                    {
                        return CommandResponse.Error(cmd.TaskId, "Beam does not have a linear location curve.").ToJson();
                    }
                }

                tx.Commit();

                var result = StructuralUtils.Default.SnapshotBeam(beam, doc);
                return CommandResponse.Success(cmd.TaskId, result, "Beam modified successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the modification: {ex.Message}").ToJson();
            }
        }

        private static bool HasAnyChange(ModifyBeamParams p) =>
            p.StartX.HasValue || p.StartY.HasValue || p.StartZ.HasValue ||
            p.EndX.HasValue || p.EndY.HasValue || p.EndZ.HasValue ||
            (p.SymbolId.HasValue && p.SymbolId.Value > 0);
    }

    /// <summary>
    /// Typed parameter bag for <see cref="ModifyBeamHandler"/>.
    /// </summary>
    public class ModifyBeamParams
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

        [Param("symbol_id")]
        public int? SymbolId { get; set; }
    }
}
