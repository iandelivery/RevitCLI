using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Structural
{
    /// <summary>
    /// Creates a beam between two points on a level. The family type is
    /// optional — when omitted, the first loaded structural framing type is
    /// used. Coordinates are in millimeters.
    /// </summary>
    public class CreateBeamHandler : DocumentCommandBase
    {
        public override string CommandName => "create_beam";
        public override string Description => "Creates a beam between two points on a level (type optional, falls back to the first loaded structural framing type)";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "beam_create" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "level_id", Type = "int", Required = true, Description = "Reference level element ID for the beam" },
            new CommandParamSchema { Name = "start_x", Type = "double", Required = true, Description = "Start point X in millimeters" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = true, Description = "Start point Y in millimeters" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = true, Description = "Start point Z in millimeters" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = true, Description = "End point X in millimeters" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = true, Description = "End point Y in millimeters" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = true, Description = "End point Z in millimeters" },
            new CommandParamSchema { Name = "start_level_offset", Type = "double", Required = false, Description = "Start level offset in millimeters", Default = 0 },
            new CommandParamSchema { Name = "end_level_offset", Type = "double", Required = false, Description = "End level offset in millimeters", Default = 0 },
            new CommandParamSchema { Name = "symbol_id", Type = "int", Required = false, Description = "FamilySymbol ID of the beam type; defaults to the first loaded structural framing type (list via get_family_symbols --category OST_StructuralFraming)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_beam\", \"parameters\": { \"level_id\": 3001, \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 6000, \"end_y\": 0, \"end_z\": 3000 } }",
            "{ \"command\": \"create_beam\", \"parameters\": { \"level_id\": 3001, \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 6000, \"end_y\": 0, \"end_z\": 3000, \"start_level_offset\": 50, \"end_level_offset\": -100, \"symbol_id\": 3417 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<CreateBeamParams>(cmd, out var error);
            if (p is null) return error!;

            var level = doc.GetElement(new ElementId(p.LevelId)) as Level;
            if (level is null)
                return CommandResponse.Error(cmd.TaskId, $"Level with ID {p.LevelId} not found.").ToJson();

            var symbol = StructuralUtils.Default.ResolveSymbol(doc, p.SymbolId, BuiltInCategory.OST_StructuralFraming);
            if (symbol is null)
            {
                if (p.SymbolId.HasValue && p.SymbolId.Value > 0)
                    return CommandResponse.Error(cmd.TaskId,
                        $"FamilySymbol with ID {p.SymbolId.Value} not found or is not a structural framing type.").ToJson();
                return CommandResponse.Error(cmd.TaskId,
                    "No structural framing family types are loaded in the document.").ToJson();
            }

            var start = new XYZ(p.StartX.MillimeterToFeet(), p.StartY.MillimeterToFeet(), p.StartZ.MillimeterToFeet());
            var end = new XYZ(p.EndX.MillimeterToFeet(), p.EndY.MillimeterToFeet(), p.EndZ.MillimeterToFeet());

            if (start.DistanceTo(end) < StructuralUtils.MinSegmentLengthFeet)
                return CommandResponse.Error(cmd.TaskId,
                    "Start and end points are too close; the beam segment must be longer than ~2.5 mm.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Beam", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();

                if (!symbol.IsActive)
                    symbol.Activate();

                var beam = doc.Create.NewFamilyInstance(Line.CreateBound(start, end), symbol, level, StructuralType.Beam);

                if (p.StartLevelOffset.HasValue)
                {
                    var off = beam.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION);
                    if (off is null || !off.Set(p.StartLevelOffset.Value.MillimeterToFeet()))
                        return CommandResponse.Error(cmd.TaskId,
                            "The created beam does not support the Start Level Offset parameter.").ToJson();
                }

                if (p.EndLevelOffset.HasValue)
                {
                    var off = beam.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION);
                    if (off is null || !off.Set(p.EndLevelOffset.Value.MillimeterToFeet()))
                        return CommandResponse.Error(cmd.TaskId,
                            "The created beam does not support the End Level Offset parameter.").ToJson();
                }

                // Snapshot before commit: in dry-run mode the rollback deletes
                // the new element, so its data must be captured up front.
                var result = StructuralUtils.Default.SnapshotBeam(beam, doc);

                tx.Commit();

                return CommandResponse.Success(cmd.TaskId, result, "Beam created successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the creation: {ex.Message}").ToJson();
            }
        }
    }

    /// <summary>
    /// Typed parameter bag for <see cref="CreateBeamHandler"/>.
    /// </summary>
    public class CreateBeamParams
    {
        [Param("level_id", Required = true)]
        public int LevelId { get; set; }

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

        [Param("start_level_offset")]
        public double? StartLevelOffset { get; set; }

        [Param("end_level_offset")]
        public double? EndLevelOffset { get; set; }

        [Param("symbol_id")]
        public int? SymbolId { get; set; }
    }
}
