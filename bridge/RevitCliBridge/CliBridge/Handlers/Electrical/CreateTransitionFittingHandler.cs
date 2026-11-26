using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers;
using RevitCliBridge.Handlers.Mep;

namespace RevitCliBridge.Handlers.Electrical
{
    /// <summary>
    /// Creates a transition fitting between two collinear cable tray
    /// connectors of differing cross-section (width or height). When the
    /// cross-sections are identical, use <c>create_union_fitting</c>
    /// instead.
    /// </summary>
    public class CreateTransitionFittingHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["width_1"] = UnitKinds.Length,
                ["height_1"] = UnitKinds.Length,
                ["width_2"] = UnitKinds.Length,
                ["height_2"] = UnitKinds.Length
            };

        public override string CommandName => "create_transition_fitting";
        public override string Description => "Creates a transition fitting between two collinear cable trays of differing sizes";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "transition_fitting" };
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id_1", Type = "int", Required = true, Description = "First cable tray element ID" },
            new CommandParamSchema { Name = "element_id_2", Type = "int", Required = true, Description = "Second cable tray element ID" },
            new CommandParamSchema { Name = "connector_index_1", Type = "int", Required = false, Description = "Connector index on first tray (default: auto-closest)" },
            new CommandParamSchema { Name = "connector_index_2", Type = "int", Required = false, Description = "Connector index on second tray (default: auto-closest)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_transition_fitting\", \"parameters\": { \"element_id_1\": 12345, \"element_id_2\": 12346 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<TransitionParams>(cmd, out var error);
            if (p is null) return error!;

            var (c1, c2, resolveError) = MepFittingResolver.ResolveConnectorPair<CableTray>(
                doc, p.ElementId1, p.ElementId2, p.ConnectorIndex1, p.ConnectorIndex2, "cable tray", 2);
            if (resolveError is not null)
                return CommandResponse.Error(cmd.TaskId, resolveError).ToJson();

            var collinearError = MepCurveGeometry.ValidateCollinearPair(c1!, c2!);
            if (collinearError is not null)
                return CommandResponse.Error(cmd.TaskId, collinearError).ToJson();

            var tray1 = (CableTray)c1!.Owner;
            var tray2 = (CableTray)c2!.Owner;
            var (w1, h1) = CableTrayUtils.Default.GetSizeInternal(tray1);
            var (w2, h2) = CableTrayUtils.Default.GetSizeInternal(tray2);
            if (Math.Abs(w1 - w2) < 0.01.MillimeterToFeet() && Math.Abs(h1 - h2) < 0.01.MillimeterToFeet())
                return CommandResponse.Error(cmd.TaskId,
                    $"Both trays have the same size ({w1.FeetToMillimeter():F0}×{h1.FeetToMillimeter():F0} mm). Use create_union_fitting for same-size joining.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Transition Fitting", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var fitting = doc.Create.NewTransitionFitting(c1!, c2!);
                tx.Commit();

                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId,
                    new
                    {
                        fitting_id = ElementIdCompat.ValueOf(fitting.Id),
                        width_1 = w1,
                        height_1 = h1,
                        width_2 = w2,
                        height_2 = h2
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

    public class TransitionParams
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
