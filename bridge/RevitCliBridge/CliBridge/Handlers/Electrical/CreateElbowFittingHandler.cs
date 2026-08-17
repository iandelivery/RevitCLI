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
    /// Creates an elbow fitting between two cable tray connectors. The
    /// connectors must belong to different elements, share the same domain,
    /// and form an angle between 2° and 95°. The closest connector pair is
    /// auto-selected when explicit indices are not provided.
    /// </summary>
    public class CreateElbowFittingHandler : DocumentCommandBase
    {
        public override string CommandName => "create_elbow_fitting";
        public override string Description => "Creates an elbow fitting between two cable tray connectors";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "elbow_fitting" };
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
            "{ \"command\": \"create_elbow_fitting\", \"parameters\": { \"element_id_1\": 12345, \"element_id_2\": 12346 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<ElbowParams>(cmd, out var error);
            if (p is null) return error!;

            var (c1, c2, resolveError) = FittingHelper.ResolveConnectorPair(
                doc, p.ElementId1, p.ElementId2, p.ConnectorIndex1, p.ConnectorIndex2);
            if (resolveError is not null)
                return CommandResponse.Error(cmd.TaskId, resolveError).ToJson();

            var validationError = CableTrayUtils.ValidateElbowPair(c1!, c2!);
            if (validationError is not null)
                return CommandResponse.Error(cmd.TaskId, validationError).ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Elbow Fitting", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var fitting = doc.Create.NewElbowFitting(c1!, c2!);
                tx.Commit();

                return CommandResponse.Success(cmd.TaskId,
                    new { fitting_id = fitting.Id.IntegerValue },
                    "Elbow fitting created successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the elbow: {ex.Message}").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit could not create the elbow: {ex.Message}").ToJson();
            }
        }
    }

    public class ElbowParams
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
