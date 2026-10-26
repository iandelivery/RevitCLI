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
    /// Creates a union fitting between two collinear pipe connectors of
    /// identical diameter. When the diameters differ, use
    /// <c>create_pipe_transition_fitting</c> instead.
    /// </summary>
    public class CreateUnionFittingHandler : DocumentCommandBase
    {
        public override string CommandName => "create_pipe_union_fitting";
        public override string Description => "Creates a union fitting between two collinear pipes of identical diameter";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "pipe_union_fitting" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id_1", Type = "int", Required = true, Description = "First pipe element ID" },
            new CommandParamSchema { Name = "element_id_2", Type = "int", Required = true, Description = "Second pipe element ID" },
            new CommandParamSchema { Name = "connector_index_1", Type = "int", Required = false, Description = "Connector index on first pipe (default: auto-closest)" },
            new CommandParamSchema { Name = "connector_index_2", Type = "int", Required = false, Description = "Connector index on second pipe (default: auto-closest)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_pipe_union_fitting\", \"parameters\": { \"element_id_1\": 12345, \"element_id_2\": 12346 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<PipeUnionParams>(cmd, out var error);
            if (p is null) return error!;

            var (c1, c2, resolveError) = MepFittingResolver.ResolveConnectorPair<Pipe>(
                doc, p.ElementId1, p.ElementId2, p.ConnectorIndex1, p.ConnectorIndex2, "pipe", 2);
            if (resolveError is not null)
                return CommandResponse.Error(cmd.TaskId, resolveError).ToJson();

            var collinearError = MepCurveGeometry.ValidateCollinearPair(c1!, c2!);
            if (collinearError is not null)
                return CommandResponse.Error(cmd.TaskId, collinearError).ToJson();

            // Size check: diameters must match for a union.
            var pipe1 = (Pipe)c1!.Owner;
            var pipe2 = (Pipe)c2!.Owner;
            double dia1 = PipeUtils.Default.GetDiameterMm(pipe1);
            double dia2 = PipeUtils.Default.GetDiameterMm(pipe2);

            if (Math.Abs(dia1 - dia2) >= 0.01)
                return CommandResponse.Error(cmd.TaskId,
                    $"Pipe diameters differ. Use create_pipe_transition_fitting for size changes.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Pipe Union Fitting", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var fitting = doc.Create.NewUnionFitting(c1!, c2!);
                tx.Commit();

                return CommandResponse.Success(cmd.TaskId,
                    new
                    {
                        fitting_id = fitting.Id.IntegerValue,
                        diameter_mm = dia1
                    },
                    "Union fitting created successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the union: {ex.Message}").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit could not create the union: {ex.Message}").ToJson();
            }
        }
    }

    public class PipeUnionParams
    {
        [Param("element_id_1", Required = true)]
        public int ElementId1 { get; set; }

        [Param("element_id_2", Required = true)]
        public int ElementId2 { get; set; }

        [Param("connector_index_1")]
        public int? ConnectorIndex1 { get; set; }

        [Param("connector_index_2")]
        public int? ConnectorIndex2 { get; set; }
    }
}