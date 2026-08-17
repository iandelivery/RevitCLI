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
    /// Creates a cross fitting where two branch ducts meet a main duct that
    /// has been pre-split at the intersection point. The caller must pass the
    /// two main halves (already split) plus the two branches. All four
    /// connectors must meet at approximately the same point, and both
    /// branches must be perpendicular to the main within 1°.
    /// </summary>
    public class CreateCrossFittingHandler : DocumentCommandBase
    {
        public override string CommandName => "create_duct_cross_fitting";
        public override string Description => "Creates a cross fitting at the meeting point of two main halves and two branch ducts";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "duct_cross_fitting" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "main_element_id_1", Type = "int", Required = true, Description = "First half of the pre-split main duct" },
            new CommandParamSchema { Name = "main_element_id_2", Type = "int", Required = true, Description = "Second half of the pre-split main duct" },
            new CommandParamSchema { Name = "branch_element_id_1", Type = "int", Required = true, Description = "First branch duct" },
            new CommandParamSchema { Name = "branch_element_id_2", Type = "int", Required = true, Description = "Second branch duct" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_duct_cross_fitting\", \"parameters\": { \"main_element_id_1\": 12345, \"main_element_id_2\": 12346, \"branch_element_id_1\": 12347, \"branch_element_id_2\": 12348 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<DuctCrossParams>(cmd, out var error);
            if (p is null) return error!;

            // Distinct-element check
            if (p.MainElementId1 == p.MainElementId2 || p.MainElementId1 == p.BranchElementId1 ||
                p.MainElementId1 == p.BranchElementId2 || p.MainElementId2 == p.BranchElementId1 ||
                p.MainElementId2 == p.BranchElementId2 || p.BranchElementId1 == p.BranchElementId2)
                return CommandResponse.Error(cmd.TaskId, "All four element IDs must refer to distinct elements.").ToJson();

            var main1 = doc.GetElement(new ElementId(p.MainElementId1)) as Duct;
            var main2 = doc.GetElement(new ElementId(p.MainElementId2)) as Duct;
            var branch1 = doc.GetElement(new ElementId(p.BranchElementId1)) as Duct;
            var branch2 = doc.GetElement(new ElementId(p.BranchElementId2)) as Duct;
            if (main1 is null || main2 is null || branch1 is null || branch2 is null)
                return CommandResponse.Error(cmd.TaskId, "One or more element IDs do not refer to a duct.").ToJson();

            var main1Curve = (main1.Location as LocationCurve)?.Curve;
            var main2Curve = (main2.Location as LocationCurve)?.Curve;
            var branch1Curve = (branch1.Location as LocationCurve)?.Curve;
            var branch2Curve = (branch2.Location as LocationCurve)?.Curve;
            if (main1Curve is null || main2Curve is null || branch1Curve is null || branch2Curve is null)
                return CommandResponse.Error(cmd.TaskId, "One or more ducts have no location curve.").ToJson();

            // The two main halves should share an endpoint (the split point).
            var m1End = main1Curve.GetEndPoint(1);
            var m2Start = main2Curve.GetEndPoint(0);
            var m1Start = main1Curve.GetEndPoint(0);
            var m2End = main2Curve.GetEndPoint(1);
            XYZ? junction = null;
            if (m1End.DistanceTo(m2Start) < DuctUtils.MinSegmentLengthFeet * 2)
                junction = m1End;
            else if (m1Start.DistanceTo(m2End) < DuctUtils.MinSegmentLengthFeet * 2)
                junction = m1Start;
            else if (m1End.DistanceTo(m2End) < DuctUtils.MinSegmentLengthFeet * 2)
                junction = m1End;
            else if (m1Start.DistanceTo(m2Start) < DuctUtils.MinSegmentLengthFeet * 2)
                junction = m1Start;

            if (junction is null)
                return CommandResponse.Error(cmd.TaskId, "The two main halves do not share a common endpoint. Pre-split the main at the intersection before calling this command.").ToJson();

            // Resolve the 4 connectors closest to the junction.
            var c1 = DuctUtils.FindClosestConnector(main1, junction);
            var c2 = DuctUtils.FindClosestConnector(main2, junction);
            var c3 = DuctUtils.FindClosestConnector(branch1, junction);
            var c4 = DuctUtils.FindClosestConnector(branch2, junction);
            if (c1 is null || c2 is null || c3 is null || c4 is null)
                return CommandResponse.Error(cmd.TaskId, "Could not resolve all four connectors at the junction point.").ToJson();

            // Domain check — all four must match.
            if (c1.Domain != c2.Domain || c1.Domain != c3.Domain || c1.Domain != c4.Domain)
                return CommandResponse.Error(cmd.TaskId, "All four connectors must share the same domain.").ToJson();

            // Perpendicularity check for both branches.
            var mainDir = main1Curve.GetEndPoint(1).Subtract(main1Curve.GetEndPoint(0)).Normalize();
            string? perpError = ValidatePerpendicular(mainDir, branch1Curve, "branch 1");
            if (perpError is not null)
                return CommandResponse.Error(cmd.TaskId, perpError).ToJson();
            perpError = ValidatePerpendicular(mainDir, branch2Curve, "branch 2");
            if (perpError is not null)
                return CommandResponse.Error(cmd.TaskId, perpError).ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create Duct Cross Fitting", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                var fitting = doc.Create.NewCrossFitting(c1, c2, c3, c4);
                tx.Commit();

                return CommandResponse.Success(cmd.TaskId,
                    new
                    {
                        fitting_id = fitting.Id.IntegerValue,
                        junction = new
                        {
                            x = junction.X.FeetToMillimeter(),
                            y = junction.Y.FeetToMillimeter(),
                            z = junction.Z.FeetToMillimeter()
                        }
                    },
                    "Cross fitting created successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the cross: {ex.Message}").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit could not create the cross: {ex.Message}").ToJson();
            }
        }

        private static string? ValidatePerpendicular(XYZ mainDir, Curve branchCurve, string label)
        {
            var branchDir = branchCurve.GetEndPoint(1).Subtract(branchCurve.GetEndPoint(0)).Normalize();
            double dot = Math.Abs(mainDir.DotProduct(branchDir));
            double angleDeg = Math.Acos(Math.Min(1.0, dot)) * 180.0 / Math.PI;
            if (Math.Abs(angleDeg - 90.0) > DuctUtils.PerpendicularityToleranceDeg)
                return $"{label} is not perpendicular to the main (angle={angleDeg:F1}°, required 89°–91°).";
            return null;
        }
    }

    public class DuctCrossParams
    {
        [Param("main_element_id_1", Required = true)]
        public int MainElementId1 { get; set; }

        [Param("main_element_id_2", Required = true)]
        public int MainElementId2 { get; set; }

        [Param("branch_element_id_1", Required = true)]
        public int BranchElementId1 { get; set; }

        [Param("branch_element_id_2", Required = true)]
        public int BranchElementId2 { get; set; }
    }
}
