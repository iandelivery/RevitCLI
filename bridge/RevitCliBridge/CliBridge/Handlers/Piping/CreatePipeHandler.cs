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
    /// Creates a straight pipe segment between two 3D points. Coordinates use
    /// the request length unit (default mm). Pipes require a piping system
    /// type (e.g. DomesticColdWater) at creation time — use
    /// <c>get_pipe_system_types</c> to discover available IDs. Pipes are
    /// round, so only <c>diameter</c> applies.
    /// </summary>
    public class CreatePipeHandler : DocumentCommandBase
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

        public override string CommandName => "create_pipe";
        public override string Description => "Creates a straight pipe segment between two 3D points";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "pipe_create" };
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
            new CommandParamSchema { Name = "level_id", Type = "int", Required = true, Description = "Level element ID to place the pipe on" },
            new CommandParamSchema { Name = "system_type_id", Type = "int", Required = true, Description = "Piping system type ID (use get_pipe_system_types to discover)" },
            new CommandParamSchema { Name = "pipe_type_id", Type = "int", Required = false, Description = "Pipe type ID (defaults to first available)" },
            new CommandParamSchema { Name = "diameter", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Diameter (request length unit, default mm)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_pipe\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 5000, \"end_y\": 0, \"end_z\": 3000, \"level_id\": 3001, \"system_type_id\": 12345 } }",
            "{ \"command\": \"create_pipe\", \"parameters\": { \"start_x\": 0, \"start_y\": 0, \"start_z\": 3000, \"end_x\": 5000, \"end_y\": 0, \"end_z\": 3000, \"level_id\": 3001, \"system_type_id\": 12345, \"diameter\": 100 } }"
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

            var start = new XYZ(p.StartX, p.StartY, p.StartZ);
            var end = new XYZ(p.EndX, p.EndY, p.EndZ);

            if (start.DistanceTo(end) < MepCurveGeometry.MinSegmentLengthFeet)
                return CommandResponse.Error(cmd.TaskId, "Start and end points are too close; segment must be at least ~8.5 mm long.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Pipe", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var pipe = Pipe.Create(doc, systemType.Id, pipeType.Id, ElementIdCompat.FromValue(p.LevelId), start, end);

                if (p.Diameter > 0)
                    pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(p.Diameter);

                tx.Commit();

                var result = PipeUtils.Default.Snapshot(pipe, doc);
                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Pipe created successfully.", tx.ResolvedFailures).ToJson();
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

        [Param("diameter")]
        public double Diameter { get; set; }
    }
}