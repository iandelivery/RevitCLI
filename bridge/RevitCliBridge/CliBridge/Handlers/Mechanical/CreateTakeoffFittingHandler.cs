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
    /// Creates a takeoff fitting (tap) where a branch duct connects to the
    /// body of a main duct. Unlike tee/cross fittings, the takeoff does not
    /// require the main to be split — Revit automatically places the tap on
    /// the main duct's body at the branch connector location. This API is
    /// duct-specific: cable trays reject <c>NewTakeoffFitting</c>.
    /// </summary>
    public class CreateTakeoffFittingHandler : DocumentCommandBase
    {
        public override string CommandName => "create_duct_takeoff_fitting";
        public override string Description => "Creates a takeoff fitting where a branch duct taps into a main duct body";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "duct_takeoff_fitting", "create_takeoff_fitting" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "branch_element_id", Type = "int", Required = true, Description = "Branch duct element ID (must have a free connector)" },
            new CommandParamSchema { Name = "main_element_id", Type = "int", Required = true, Description = "Main duct element ID (the tap is placed on its body)" },
            new CommandParamSchema { Name = "branch_connector_index", Type = "int", Required = false, Description = "Connector index on the branch (default: auto-closest to main)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_duct_takeoff_fitting\", \"parameters\": { \"branch_element_id\": 12346, \"main_element_id\": 12345 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<DuctTakeoffParams>(cmd, out var error);
            if (p is null) return error!;

            if (p.BranchElementId == p.MainElementId)
                return CommandResponse.Error(cmd.TaskId, "Branch and main element IDs must be different.").ToJson();

            var branch = doc.GetElement(new ElementId(p.BranchElementId)) as Duct;
            var main = doc.GetElement(new ElementId(p.MainElementId)) as Duct;
            if (branch is null || main is null)
                return CommandResponse.Error(cmd.TaskId, "One or both element IDs do not refer to a duct.").ToJson();

            var mainCurve = (main.Location as LocationCurve)?.Curve;
            if (mainCurve is null)
                return CommandResponse.Error(cmd.TaskId, "Main duct has no location curve.").ToJson();

            // Resolve the branch connector.
            Connector? branchConnector;
            if (p.BranchConnectorIndex.HasValue)
            {
                var list = new List<Connector>();
                foreach (Connector c in branch.ConnectorManager.Connectors)
                    list.Add(c);
                int idx = p.BranchConnectorIndex.Value;
                if (idx < 0 || idx >= list.Count)
                    return CommandResponse.Error(cmd.TaskId, $"branch_connector_index={idx} is out of range (0–{list.Count - 1}).").ToJson();
                branchConnector = list[idx];
            }
            else
            {
                // Auto-select the branch connector closest to the main curve.
                branchConnector = FindClosestToCurve(branch, mainCurve);
            }

            if (branchConnector is null)
                return CommandResponse.Error(cmd.TaskId, "Could not resolve a branch connector.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Duct Takeoff Fitting", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                // NewTakeoffFitting takes (branchConnector, mainMEPCurve).
                // Revit places the tap on the main body automatically.
                var fitting = doc.Create.NewTakeoffFitting(branchConnector, main);
                tx.Commit();

                return CommandResponse.Success(cmd.TaskId,
                    new { fitting_id = fitting.Id.IntegerValue },
                    "Takeoff fitting created successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the takeoff: {ex.Message}").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit could not create the takeoff: {ex.Message}").ToJson();
            }
        }

        private static Connector? FindClosestToCurve(Duct branch, Curve mainCurve)
        {
            Connector? best = null;
            double minDist = double.MaxValue;

            foreach (Connector c in branch.ConnectorManager.Connectors)
            {
                double d = mainCurve.Distance(c.Origin);
                if (d < minDist)
                {
                    minDist = d;
                    best = c;
                }
            }
            return best;
        }
    }

    public class DuctTakeoffParams
    {
        [Param("branch_element_id", Required = true)]
        public int BranchElementId { get; set; }

        [Param("main_element_id", Required = true)]
        public int MainElementId { get; set; }

        [Param("branch_connector_index")]
        public int? BranchConnectorIndex { get; set; }
    }
}
