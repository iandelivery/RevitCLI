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
    /// one optional field must be provided. Coordinates are in millimeters.
    /// </summary>
    public class ModifyPipeHandler : DocumentCommandBase
    {
        public override string CommandName => "modify_pipe";
        public override string Description => "Modifies an existing pipe (endpoints, type, diameter)";
        public override string Category => "Modify";
        public override string[] Aliases => new[] { "pipe_modify" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Pipe element ID to modify" },
            new CommandParamSchema { Name = "start_x", Type = "double", Required = false, Description = "New start X in millimeters" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = false, Description = "New start Y in millimeters" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = false, Description = "New start Z in millimeters" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = false, Description = "New end X in millimeters" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = false, Description = "New end Y in millimeters" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = false, Description = "New end Z in millimeters" },
            new CommandParamSchema { Name = "pipe_type_id", Type = "int", Required = false, Description = "New pipe type ID" },
            new CommandParamSchema { Name = "diameter_mm", Type = "double", Required = false, Description = "New diameter in millimeters" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"modify_pipe\", \"parameters\": { \"element_id\": 12345, \"end_x\": 6000 } }",
            "{ \"command\": \"modify_pipe\", \"parameters\": { \"element_id\": 12345, \"diameter_mm\": 150 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<ModifyPipeParams>(cmd, out var error);
            if (p is null) return error!;

            if (!HasAnyChange(p))
                return CommandResponse.Error(cmd.TaskId, "At least one of start/end coordinates, pipe_type_id, or diameter_mm must be provided.").ToJson();

            var pipe = doc.GetElement(new ElementId(p.ElementId)) as Pipe;
            if (pipe is null)
                return CommandResponse.Error(cmd.TaskId, $"Element {p.ElementId} is not a pipe.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Modify Pipe", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();

                // Type change
                if (p.PipeTypeId.HasValue && p.PipeTypeId.Value > 0 && p.PipeTypeId.Value != pipe.PipeType?.Id.IntegerValue)
                {
                    var newType = doc.GetElement(new ElementId(p.PipeTypeId.Value)) as PipeType;
                    if (newType is null)
                        return CommandResponse.Error(cmd.TaskId, $"Element {p.PipeTypeId.Value} is not a pipe type.").ToJson();
                    pipe.ChangeTypeId(newType.Id);
                }

                // Endpoint change — must update both together (LocationCurve is a single Line)
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
                            (p.StartX ?? oldStart.X.FeetToMillimeter()).MillimeterToFeet(),
                            (p.StartY ?? oldStart.Y.FeetToMillimeter()).MillimeterToFeet(),
                            (p.StartZ ?? oldStart.Z.FeetToMillimeter()).MillimeterToFeet());

                        var newEnd = new XYZ(
                            (p.EndX ?? oldEnd.X.FeetToMillimeter()).MillimeterToFeet(),
                            (p.EndY ?? oldEnd.Y.FeetToMillimeter()).MillimeterToFeet(),
                            (p.EndZ ?? oldEnd.Z.FeetToMillimeter()).MillimeterToFeet());

                        if (newStart.DistanceTo(newEnd) < MepCurveGeometry.MinSegmentLengthFeet)
                            return CommandResponse.Error(cmd.TaskId, "New endpoints are too close; segment must be at least ~8.5 mm long.").ToJson();

                        locCurve.Curve = Line.CreateBound(newStart, newEnd);
                    }
                    else
                    {
                        return CommandResponse.Error(cmd.TaskId, "Pipe does not have a linear location curve.").ToJson();
                    }
                }

                // Diameter (pipes are round).
                if (p.DiameterMm.HasValue && p.DiameterMm.Value > 0)
                    pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(p.DiameterMm.Value.MillimeterToFeet());

                tx.Commit();

                var result = PipeUtils.Default.Snapshot(pipe, doc);
                return CommandResponse.Success(cmd.TaskId, result, "Pipe modified successfully.").ToJson();
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
            (p.DiameterMm.HasValue && p.DiameterMm.Value > 0);
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

        [Param("diameter_mm")]
        public double? DiameterMm { get; set; }
    }
}