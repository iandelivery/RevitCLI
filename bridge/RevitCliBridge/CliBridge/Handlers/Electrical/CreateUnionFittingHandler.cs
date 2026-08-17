using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers;

namespace RevitCliBridge.Handlers.Electrical
{
    /// <summary>
    /// Creates a union fitting between two collinear cable tray connectors of
    /// identical cross-section. When the cross-sections differ, use
    /// <c>create_transition_fitting</c> instead.
    /// </summary>
    public class CreateUnionFittingHandler : DocumentCommandBase
    {
        public override string CommandName => "create_union_fitting";
        public override string Description => "Creates a union fitting between two collinear cable trays of identical size";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "union_fitting" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id_1", Type = "int", Required = true, Description = "First cable tray element ID" },
            new CommandParamSchema { Name = "element_id_2", Type = "int", Required = true, Description = "Second cable tray element ID" },
            new CommandParamSchema { Name = "connector_index_1", Type = "int", Required = false, Description = "Connector index on first tray (default: auto-closest)" },
            new CommandParamSchema { Name = "connector_index_2", Type = "int", Required = false, Description = "Connector index on second tray (default: auto-closest)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_union_fitting\", \"parameters\": { \"element_id_1\": 12345, \"element_id_2\": 12346 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<UnionParams>(cmd, out var error);
            if (p is null) return error!;

            var (c1, c2, resolveError) = FittingHelper.ResolveConnectorPair(
                doc, p.ElementId1, p.ElementId2, p.ConnectorIndex1, p.ConnectorIndex2);
            if (resolveError is not null)
                return CommandResponse.Error(cmd.TaskId, resolveError).ToJson();

            var collinearError = FittingHelper.ValidateCollinearPair(c1!, c2!);
            if (collinearError is not null)
                return CommandResponse.Error(cmd.TaskId, collinearError).ToJson();

            // Size check: cross-sections must match for a union.
            var tray1 = (CableTray)c1!.Owner;
            var tray2 = (CableTray)c2!.Owner;
            var (w1, h1) = CableTrayUtils.GetSize(tray1);
            var (w2, h2) = CableTrayUtils.GetSize(tray2);
            if (Math.Abs(w1 - w2) >= 0.01 || Math.Abs(h1 - h2) >= 0.01)
                return CommandResponse.Error(cmd.TaskId,
                    $"Tray sizes differ ({w1:F0}×{h1:F0} vs {w2:F0}×{h2:F0} mm). Use create_transition_fitting for size changes.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Union Fitting", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var fitting = doc.Create.NewUnionFitting(c1!, c2!);
                tx.Commit();

                return CommandResponse.Success(cmd.TaskId,
                    new
                    {
                        fitting_id = fitting.Id.IntegerValue,
                        size = new { width_mm = w1, height_mm = h1 }
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

    public class UnionParams
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
