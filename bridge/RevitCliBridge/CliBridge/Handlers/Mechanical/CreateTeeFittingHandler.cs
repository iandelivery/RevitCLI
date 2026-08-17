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
    /// Creates a tee fitting where a branch duct meets a main duct. The main
    /// is auto-split by Revit when the same main connector is passed twice
    /// to <c>NewTeeFitting</c> — no manual pre-splitting is required. The
    /// branch must be approximately perpendicular to the main (within 1°).
    /// </summary>
    public class CreateTeeFittingHandler : DocumentCommandBase
    {
        public override string CommandName => "create_duct_tee_fitting";
        public override string Description => "Creates a tee fitting where a branch duct meets a main duct";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "duct_tee_fitting" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "main_element_id", Type = "int", Required = true, Description = "Main duct element ID (will be auto-split at the intersection)" },
            new CommandParamSchema { Name = "branch_element_id", Type = "int", Required = true, Description = "Branch duct element ID" },
            new CommandParamSchema { Name = "branch_connector_index", Type = "int", Required = false, Description = "Connector index on the branch (default: auto-closest to intersection)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_duct_tee_fitting\", \"parameters\": { \"main_element_id\": 12345, \"branch_element_id\": 12346 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<DuctTeeParams>(cmd, out var error);
            if (p is null) return error!;

            if (p.MainElementId == p.BranchElementId)
                return CommandResponse.Error(cmd.TaskId, "Main and branch element IDs must be different.").ToJson();

            var main = doc.GetElement(new ElementId(p.MainElementId)) as Duct;
            var branch = doc.GetElement(new ElementId(p.BranchElementId)) as Duct;
            if (main is null || branch is null)
                return CommandResponse.Error(cmd.TaskId, "One or both element IDs do not refer to a duct.").ToJson();

            var mainCurve = (main.Location as LocationCurve)?.Curve;
            var branchCurve = (branch.Location as LocationCurve)?.Curve;
            if (mainCurve is null || branchCurve is null)
                return CommandResponse.Error(cmd.TaskId, "Duct has no location curve.").ToJson();

            var intersection = DuctUtils.ComputeIntersection(mainCurve, branchCurve);
            if (intersection is null)
                return CommandResponse.Error(cmd.TaskId, "Branch duct does not intersect the main duct.").ToJson();

            // Resolve connectors nearest the intersection.
            var mainConnector = DuctUtils.FindClosestConnector(main, intersection);
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
                branchConnector = DuctUtils.FindClosestConnector(branch, intersection);
            }

            if (mainConnector is null || branchConnector is null)
                return CommandResponse.Error(cmd.TaskId, "Could not resolve connectors at the intersection point.").ToJson();

            // Domain check
            if (mainConnector.Domain != branchConnector.Domain)
                return CommandResponse.Error(cmd.TaskId, "Main and branch connectors are in different domains.").ToJson();

            // Perpendicularity check (~1° tolerance — Revit throws otherwise).
            var mainDir = mainCurve.GetEndPoint(1).Subtract(mainCurve.GetEndPoint(0)).Normalize();
            var branchDir = branchCurve.GetEndPoint(1).Subtract(branchCurve.GetEndPoint(0)).Normalize();
            double curveDot = Math.Abs(mainDir.DotProduct(branchDir));
            double intersectionAngleDeg = Math.Acos(Math.Min(1.0, curveDot)) * 180.0 / Math.PI;
            if (Math.Abs(intersectionAngleDeg - 90.0) > DuctUtils.PerpendicularityToleranceDeg)
                return CommandResponse.Error(cmd.TaskId,
                    $"Branch is not perpendicular to the main (angle={intersectionAngleDeg:F1}°, required 89°–91°).").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Duct Tee Fitting", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                // Auto-split trick: pass the same main connector twice. Revit
                // internally breaks the main into two segments and creates the
                // tee. This avoids manual delete+recreate and keeps the
                // operation atomic.
                var fitting = doc.Create.NewTeeFitting(mainConnector, mainConnector, branchConnector);
                tx.Commit();

                return CommandResponse.Success(cmd.TaskId,
                    new
                    {
                        fitting_id = fitting.Id.IntegerValue,
                        intersection = new
                        {
                            x = intersection.X.FeetToMillimeter(),
                            y = intersection.Y.FeetToMillimeter(),
                            z = intersection.Z.FeetToMillimeter()
                        }
                    },
                    "Tee fitting created successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the tee: {ex.Message}").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit could not create the tee: {ex.Message}").ToJson();
            }
        }
    }

    public class DuctTeeParams
    {
        [Param("main_element_id", Required = true)]
        public int MainElementId { get; set; }

        [Param("branch_element_id", Required = true)]
        public int BranchElementId { get; set; }

        [Param("branch_connector_index")]
        public int? BranchConnectorIndex { get; set; }
    }
}
