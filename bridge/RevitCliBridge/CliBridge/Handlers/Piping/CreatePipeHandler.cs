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
    /// Creates a straight pipe segment between two 3D points. Coordinates are
    /// accepted in millimeters and converted to internal feet. Pipes require a
    /// piping system type (e.g. DomesticColdWater) at creation time — use
    /// <c>get_pipe_system_types</c> to discover available IDs. Pipes are round,
    /// so only <c>diameter_mm</c> applies.
    /// </summary>
    public class CreatePipeHandler : DocumentCommandBase
    {
        public override string CommandName => "create_pipe";
        public override string Description => "Creates a straight pipe segment between two 3D points";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "pipe_create" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "start_x", Type = "double", Required = true, Description = "Start X in millimeters" },
            new CommandParamSchema { Name = "start_y", Type = "double", Required = true, Description = "Start Y in millimeters" },
            new CommandParamSchema { Name = "start_z", Type = "double", Required = true, Description = "Start Z in millimeters" },
            new CommandParamSchema { Name = "end_x", Type = "double", Required = true, Description = "End X in millimeters" },
            new CommandParamSchema { Name = "end_y", Type = "double", Required = true, Description = "End Y in millimeters" },
            new CommandParamSchema { Name = "end_z", Type = "double", Required = true, Description = "End Z in millimeters" },
            new CommandParamSchema { Name = "level_id", Type = "int", Required = true, Description = "Level element ID to place the pipe on" },
            new CommandParamSchema { Name = "system_type_id", Type = "int", Required = true, Description = "Piping system type ID (use get_pipe_system_types to discover)" },
            new CommandParamSchema { Name = "pipe_type_id", Type = "int", Required = false, Description = "Pipe type ID (defaults to first available)" },
            new CommandParamSchema { Name = "diameter_mm", Type = "double", Required = false, Description = "Diameter in millimeters" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_pipe\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 5000, \"end_y\": 0, \"end_z\": 3000, \"level_id\": 3001, \"system_type_id\": 12345 } }",
            "{ \"command\": \"create_pipe\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 5000, \"end_y\": 0, \"end_z\": 3000, \"level_id\": 3001, \"system_type_id\": 12345, \"diameter_mm\": 100 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<CreatePipeParams>(cmd, out var error);
            if (p is null) return error!;

            var pipeType = PipeUtils.Default.ResolveType(doc, p.PipeTypeId);
            if (pipeType is null)
                return CommandResponse.Error(cmd.TaskId, "No pipe type found in the document.").ToJson();

            var systemType = PipeUtils.Default.ResolveSystemType(doc, p.SystemTypeId);
            if (systemType is null)
                return CommandResponse.Error(cmd.TaskId, $"Element {p.SystemTypeId} is not a valid piping system type. Use get_pipe_system_types to discover available IDs.").ToJson();

            var start = new XYZ(p.StartX.MillimeterToFeet(), p.StartY.MillimeterToFeet(), p.StartZ.MillimeterToFeet());
            var end = new XYZ(p.EndX.MillimeterToFeet(), p.EndY.MillimeterToFeet(), p.EndZ.MillimeterToFeet());

            if (start.DistanceTo(end) < MepCurveGeometry.MinSegmentLengthFeet)
                return CommandResponse.Error(cmd.TaskId, "Start and end points are too close; segment must be at least ~8.5 mm long.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Pipe", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var pipe = Pipe.Create(doc, systemType.Id, pipeType.Id, new ElementId(p.LevelId), start, end);

                // Apply the instance diameter override (pipes are round).
                if (p.DiameterMm > 0)
                    pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(p.DiameterMm.MillimeterToFeet());

                tx.Commit();

                var result = PipeUtils.Default.Snapshot(pipe, doc);
                return CommandResponse.Success(cmd.TaskId, result, "Pipe created successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the creation: {ex.Message}").ToJson();
            }
        }
    }

    public class CreatePipeParams
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

        [Param("pipe_type_id")]
        public int? PipeTypeId { get; set; }

        [Param("diameter_mm")]
        public double DiameterMm { get; set; }
    }
}