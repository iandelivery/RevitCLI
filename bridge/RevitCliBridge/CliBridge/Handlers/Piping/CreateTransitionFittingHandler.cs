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
    /// Creates a transition fitting between two collinear pipe connectors of
    /// differing diameter. When the diameters are identical, use
    /// <c>create_pipe_union_fitting</c> instead.
    /// </summary>
    public class CreateTransitionFittingHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["diameter_1"] = UnitKinds.Length,
                ["diameter_2"] = UnitKinds.Length
            };

        public override string CommandName => "create_pipe_transition_fitting";
        public override string Description => "Creates a transition fitting between two collinear pipes of differing diameter";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "pipe_transition_fitting" };
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id_1", Type = "int", Required = true, Description = "First pipe element ID" },
            new CommandParamSchema { Name = "element_id_2", Type = "int", Required = true, Description = "Second pipe element ID" },
            new CommandParamSchema { Name = "connector_index_1", Type = "int", Required = false, Description = "Connector index on first pipe (default: auto-closest)" },
            new CommandParamSchema { Name = "connector_index_2", Type = "int", Required = false, Description = "Connector index on second pipe (default: auto-closest)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_pipe_transition_fitting\", \"parameters\": { \"element_id_1\": 12345, \"element_id_2\": 12346 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<PipeTransitionParams>(cmd, out var error);
            if (p is null) return error!;

            var (c1, c2, resolveError) = MepFittingResolver.ResolveConnectorPair<Pipe>(
                doc, p.ElementId1, p.ElementId2, p.ConnectorIndex1, p.ConnectorIndex2, "pipe", 2);
            if (resolveError is not null)
                return CommandResponse.Error(cmd.TaskId, resolveError).ToJson();

            var collinearError = MepCurveGeometry.ValidateCollinearPair(c1!, c2!);
            if (collinearError is not null)
                return CommandResponse.Error(cmd.TaskId, collinearError).ToJson();

            var pipe1 = (Pipe)c1!.Owner;
            var pipe2 = (Pipe)c2!.Owner;
            double dia1 = PipeUtils.Default.GetDiameter(pipe1);
            double dia2 = PipeUtils.Default.GetDiameter(pipe2);

            if (Math.Abs(dia1 - dia2) < 0.01.MillimeterToFeet())
                return CommandResponse.Error(cmd.TaskId,
                    $"Both pipes have the same diameter. Use create_pipe_union_fitting for same-size joining.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Pipe Transition Fitting", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var fitting = doc.Create.NewTransitionFitting(c1!, c2!);
                tx.Commit();

                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId,
                    new
                    {
                        fitting_id = ElementIdCompat.ValueOf(fitting.Id),
                        diameter_1 = dia1,
                        diameter_2 = dia2
                    },
                    "Transition fitting created successfully.", tx.ResolvedFailures).ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the transition: {ex.Message}").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit could not create the transition: {ex.Message}").ToJson();
            }
        }
    }

    public class PipeTransitionParams
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