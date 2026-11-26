using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers;
using RevitCliBridge.Handlers.Mep;

namespace RevitCliBridge.Handlers.Piping
{
    /// <summary>
    /// Modifies an existing pipe: endpoints, type, and/or diameter. At least
    /// one optional field must be provided. Coordinates use the request
    /// length unit (default mm).
    /// </summary>
    public class ModifyPipeHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["x"] = UnitKinds.Length,
                ["y"] = UnitKinds.Length,
                ["z"] = UnitKinds.Length,
                ["diameter"] = UnitKinds.Length,
                ["length"] = UnitKinds.Length
            };

        public override string CommandName => "modify_pipe";
        public override string Description => "Modifies an existing pipe (endpoints, type, diameter)";
        public override string Category => "Modify";
        public override string[] Aliases => new[] { "pipe_modify" };
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Pipe element ID to modify" },
            new CommandParamSchema { Name = "start_x", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New start X (request length unit, default mm)" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New start Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New start Z (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New end X (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New end Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New end Z (request length unit, default mm)" },
            new CommandParamSchema { Name = "pipe_type_id", Type = "int", Required = false, Description = "New pipe type ID" },
            new CommandParamSchema { Name = "diameter", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New diameter (request length unit, default mm)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"modify_pipe\", \"parameters\": { \"element_id\": 12345, \"end_x\": 6000 } }",
            "{ \"command\": \"modify_pipe\", \"parameters\": { \"element_id\": 12345, \"diameter\": 150 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<ModifyPipeParams>(cmd, out var error);
            if (p is null) return error!;

            if (!HasAnyChange(p))
                return CommandResponse.Error(cmd.TaskId, "At least one of start/end coordinates, pipe_type_id, or diameter must be provided.").ToJson();

            var pipe = doc.GetElement(ElementIdCompat.FromValue(p.ElementId)) as Pipe;
            if (pipe is null)
                return CommandResponse.Error(cmd.TaskId, $"Element {p.ElementId} is not a pipe.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Modify Pipe", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();

                if (p.PipeTypeId.HasValue && p.PipeTypeId.Value > 0 && p.PipeTypeId.Value != ElementIdCompat.ValueOf(pipe.PipeType?.Id))
                {
                    var newType = doc.GetElement(ElementIdCompat.FromValue(p.PipeTypeId.Value)) as PipeType;
                    if (newType is null)
                        return CommandResponse.Error(cmd.TaskId, $"Element {p.PipeTypeId.Value} is not a pipe type.").ToJson();
                    pipe.ChangeTypeId(newType.Id);
                }

                bool hasStart = p.StartX.HasValue || p.StartY.HasValue || p.StartZ.HasValue;
                bool hasEnd = p.EndX.HasValue || p.EndY.HasValue || p.EndZ.HasValue;
                if (hasStart || hasEnd)
                {
                    var locCurve = pipe.Location as LocationCurve;
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
                        return CommandResponse.Error(cmd.TaskId, "Pipe does not have a linear location curve.").ToJson();
                    }
                }

                if (p.Diameter.HasValue && p.Diameter.Value > 0)
                    pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(p.Diameter.Value);

                tx.Commit();

                var result = PipeUtils.Default.Snapshot(pipe, doc);
                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Pipe modified successfully.", tx.ResolvedFailures).ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the modification: {ex.Message}").ToJson();
            }
        }

        private static bool HasAnyChange(ModifyPipeParams p) =>
            p.StartX.HasValue || p.StartY.HasValue || p.StartZ.HasValue ||
            p.EndX.HasValue || p.EndY.HasValue || p.EndZ.HasValue ||
            (p.PipeTypeId.HasValue && p.PipeTypeId.Value > 0) ||
            (p.Diameter.HasValue && p.Diameter.Value > 0);
    }

    public class ModifyPipeParams
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

        [Param("pipe_type_id")]
        public int? PipeTypeId { get; set; }

        [Param("diameter")]
        public double? Diameter { get; set; }
    }
}