using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers;

namespace RevitCliBridge.Handlers.Mechanical
{
    /// <summary>
    /// Creates a union fitting between two collinear duct connectors of
    /// identical cross-section. When the cross-sections differ, use
    /// <c>create_transition_fitting</c> instead.
    /// </summary>
    public class CreateUnionFittingHandler : DocumentCommandBase
    {
        public override string CommandName => "create_duct_union_fitting";
        public override string Description => "Creates a union fitting between two collinear ducts of identical size";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "duct_union_fitting" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id_1", Type = "int", Required = true, Description = "First duct element ID" },
            new CommandParamSchema { Name = "element_id_2", Type = "int", Required = true, Description = "Second duct element ID" },
            new CommandParamSchema { Name = "connector_index_1", Type = "int", Required = false, Description = "Connector index on first duct (default: auto-closest)" },
            new CommandParamSchema { Name = "connector_index_2", Type = "int", Required = false, Description = "Connector index on second duct (default: auto-closest)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_union_fitting\", \"parameters\": { \"element_id_1\": 12345, \"element_id_2\": 12346 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<DuctUnionParams>(cmd, out var error);
            if (p is null) return error!;

            var (c1, c2, resolveError) = DuctFittingHelper.ResolveConnectorPair(
                doc, p.ElementId1, p.ElementId2, p.ConnectorIndex1, p.ConnectorIndex2);
            if (resolveError is not null)
                return CommandResponse.Error(cmd.TaskId, resolveError).ToJson();

            var collinearError = DuctFittingHelper.ValidateCollinearPair(c1!, c2!);
            if (collinearError is not null)
                return CommandResponse.Error(cmd.TaskId, collinearError).ToJson();

            // Size check: cross-sections must match for a union.
            var duct1 = (Duct)c1!.Owner;
            var duct2 = (Duct)c2!.Owner;
            var (dia1, w1, h1) = DuctUtils.GetSize(duct1);
            var (dia2, w2, h2) = DuctUtils.GetSize(duct2);

            bool differentSize = (Math.Abs(dia1 - dia2) >= 0.01 || Math.Abs(w1 - w2) >= 0.01 || Math.Abs(h1 - h2) >= 0.01);
            if (differentSize)
                return CommandResponse.Error(cmd.TaskId,
                    $"Duct sizes differ. Use create_transition_fitting for size changes.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Duct Union Fitting", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var fitting = doc.Create.NewUnionFitting(c1!, c2!);
                tx.Commit();

                return CommandResponse.Success(cmd.TaskId,
                    new
                    {
                        fitting_id = fitting.Id.IntegerValue,
                        size = new { diameter_mm = dia1, width_mm = w1, height_mm = h1 }
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

    public class DuctUnionParams
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
