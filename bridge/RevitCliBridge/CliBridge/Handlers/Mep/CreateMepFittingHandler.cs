using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers;

namespace RevitCliBridge.Handlers.Mep
{
    /// <summary>
    /// Unified, discipline-agnostic entry point for MEP fittings. An Agent
    /// (reading the generated schema / llms.txt) can discover a single
    /// <c>create_mep_fitting</c> instead of ten per-discipline variants.
    /// Dispatches to the same Revit call behind the per-discipline commands
    /// (elbow / tee / cross / transition / union / takeoff) for any of
    /// duct / pipe / cable_tray. Nothing here changes how the specialized
    /// commands behave; this is a thin broker.
    /// </summary>
    /// <remarks>
    /// Cross requires four elements (two main halves + two branches) that the
    /// compact two-id interface cannot carry — the broker rejects it and
    /// points the caller at <c>create_&lt;class&gt;_cross_fitting</c>.
    /// </remarks>
    public class CreateMepFittingHandler : DocumentCommandBase
    {
        public override string CommandName => "create_mep_fitting";
        public override string Description => "Creates an MEP fitting for any pipe/duct/cable_tray via a unified interface";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "mep_fitting" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "type", Type = "string", Required = true, Description = "Fitting type: elbow|tee|cross|transition|union|takeoff" },
            new CommandParamSchema { Name = "class", Type = "string", Required = true, Description = "MEP class: duct|pipe|cable_tray" },
            new CommandParamSchema { Name = "element_id_1", Type = "int", Required = true, Description = "First MEP element ID (for tee = main, for takeoff = branch)" },
            new CommandParamSchema { Name = "element_id_2", Type = "int", Required = true, Description = "Second MEP element ID (for tee = branch, for takeoff = main)" },
            new CommandParamSchema { Name = "connector_index_1", Type = "int", Required = false, Description = "Connector index on first element (default: auto-closest)" },
            new CommandParamSchema { Name = "connector_index_2", Type = "int", Required = false, Description = "Connector index on second element (default: auto-closest; tee/takeoff: branch connector)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_mep_fitting\", \"parameters\": { \"type\": \"elbow\", \"class\": \"pipe\", \"element_id_1\": 12345, \"element_id_2\": 12346 } }",
            "{ \"command\": \"create_mep_fitting\", \"parameters\": { \"type\": \"transition\", \"class\": \"duct\", \"element_id_1\": 12345, \"element_id_2\": 12346 } }",
            "{ \"command\": \"create_mep_fitting\", \"parameters\": { \"type\": \"tee\", \"class\": \"pipe\", \"element_id_1\": 12345, \"element_id_2\": 12346 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<MepFittingParams>(cmd, out var error);
            if (p is null) return error!;

            string type = p.Type.Trim().ToLowerInvariant();
            string cls = p.Class.Trim().ToLowerInvariant();

            if (cls != "duct" && cls != "pipe" && cls != "cable_tray")
                return CommandResponse.Error(cmd.TaskId, $"class must be one of duct|pipe|cable_tray (got \"{p.Class}\").").ToJson();

            if (type != "elbow" && type != "tee" && type != "cross" &&
                type != "transition" && type != "union" && type != "takeoff")
                return CommandResponse.Error(cmd.TaskId, $"type must be one of elbow|tee|cross|transition|union|takeoff (got \"{p.Type}\").").ToJson();

            if (type == "cross")
                return CommandResponse.Error(cmd.TaskId,
                    "The unified interface cannot carry a cross (it needs two main halves + two branches). Use create_" + cls + "_cross_fitting instead.").ToJson();

            if (p.ElementId1 == p.ElementId2)
                return CommandResponse.Error(cmd.TaskId, "element_id_1 and element_id_2 must refer to distinct elements.").ToJson();

            // Resolve both elements as MEPCurve of the requested class.
            var e1 = ResolveElement(doc, p.ElementId1, cls, out var label1);
            var e2 = ResolveElement(doc, p.ElementId2, cls, out var label2);
            if (e1 is null || e2 is null)
                return CommandResponse.Error(cmd.TaskId, $"{label1 ?? label2} is not a {cls}.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Create MEP Fitting", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();
                Element? fitting = null;
                string verb = "";

                switch (type)
                {
                    case "elbow":
                    {
                        var (c1, c2, rerr) = MepFittingResolver.ResolveConnectorPair<MEPCurve>(
                            doc, p.ElementId1, p.ElementId2, p.ConnectorIndex1, p.ConnectorIndex2, cls, 2);
                        if (rerr is not null) return CommandResponse.Error(cmd.TaskId, rerr).ToJson();
                        var verr = MepCurveGeometry.ValidateElbowPair(c1!, c2!);
                        if (verr is not null) return CommandResponse.Error(cmd.TaskId, verr).ToJson();
                        fitting = doc.Create.NewElbowFitting(c1!, c2!);
                        verb = "Elbow";
                        break;
                    }
                    case "transition":
                    case "union":
                    {
                        var (c1, c2, rerr) = MepFittingResolver.ResolveConnectorPair<MEPCurve>(
                            doc, p.ElementId1, p.ElementId2, p.ConnectorIndex1, p.ConnectorIndex2, cls, 2);
                        if (rerr is not null) return CommandResponse.Error(cmd.TaskId, rerr).ToJson();
                        var cerr = MepCurveGeometry.ValidateCollinearPair(c1!, c2!);
                        if (cerr is not null) return CommandResponse.Error(cmd.TaskId, cerr).ToJson();

                        var s1 = ReadSize(e1);
                        var s2 = ReadSize(e2);
                        bool diff = Math.Abs(s1.DiameterMm - s2.DiameterMm) >= 0.01 ||
                                    Math.Abs(s1.WidthMm - s2.WidthMm) >= 0.01 ||
                                    Math.Abs(s1.HeightMm - s2.HeightMm) >= 0.01;
                        if (type == "union" && diff)
                            return CommandResponse.Error(cmd.TaskId, "Sizes differ. Use type=transition for size changes.").ToJson();
                        if (type == "transition" && !diff)
                            return CommandResponse.Error(cmd.TaskId, "Same size. Use type=union for same-size joining.").ToJson();

                        fitting = type == "union"
                            ? doc.Create.NewUnionFitting(c1!, c2!)
                            : doc.Create.NewTransitionFitting(c1!, c2!);
                        verb = type == "union" ? "Union" : "Transition";
                        break;
                    }
                    case "tee":
                        fitting = CreateTee(doc, e1, e2, p.ConnectorIndex2, out var tieError);
                        if (tieError is not null) return CommandResponse.Error(cmd.TaskId, tieError).ToJson();
                        verb = "Tee";
                        break;
                    case "takeoff":
                    {
                        var mainCurve = (e2.Location as LocationCurve)?.Curve;
                        if (mainCurve is null) return CommandResponse.Error(cmd.TaskId, "Main element has no location curve.").ToJson();

                        Connector branchConn = PickBranchConnector(e1, p.ConnectorIndex2, mainCurve, out var branchError);
                        if (branchError is not null) return CommandResponse.Error(cmd.TaskId, branchError).ToJson();
                        if (branchConn is null) return CommandResponse.Error(cmd.TaskId, "Could not resolve a branch connector.").ToJson();

                        fitting = doc.Create.NewTakeoffFitting(branchConn, e2);
                        verb = "Takeoff";
                        break;
                    }
                }

                tx.Commit();
                return CommandResponse.Success(cmd.TaskId,
                    new { fitting_id = fitting!.Id.IntegerValue },
                    $"{verb} fitting created successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the fitting: {ex.Message}").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit could not create the fitting: {ex.Message}").ToJson();
            }
        }

        private static Element? CreateTee(Document doc, MEPCurve main, MEPCurve branch, int? branchConnectorIndex,
            out string? error)
        {
            error = null;
            var mainCurve = (main.Location as LocationCurve)?.Curve;
            var branchCurve = (branch.Location as LocationCurve)?.Curve;
            if (mainCurve is null || branchCurve is null) { error = "Element has no location curve."; return null; }

            var intersection = MepCurveGeometry.ComputeIntersection(mainCurve, branchCurve);
            if (intersection is null) { error = "Branch does not intersect the main element."; return null; }

            var mainConnector = MepCurveGeometry.FindClosestConnector(main, intersection);
            Connector branchConn = branchConnectorIndex.HasValue
                ? IndexConnector(branch, branchConnectorIndex.Value, out var idxErr)
                : MepCurveGeometry.FindClosestConnector(branch, intersection)!;
            if (branchConnectorIndex.HasValue && idxErr is not null) { error = idxErr; return null; }

            if (mainConnector is null || branchConn is null) { error = "Could not resolve connectors at the intersection point."; return null; }
            if (mainConnector.Domain != branchConn.Domain) { error = "Main and branch connectors are in different domains."; return null; }

            var mainDir = mainCurve.GetEndPoint(1).Subtract(mainCurve.GetEndPoint(0)).Normalize();
            var branchDir = branchCurve.GetEndPoint(1).Subtract(branchCurve.GetEndPoint(0)).Normalize();
            double dot = Math.Abs(mainDir.DotProduct(branchDir));
            double angleDeg = Math.Acos(Math.Min(1.0, dot)) * 180.0 / Math.PI;
            if (Math.Abs(angleDeg - 90.0) > MepCurveGeometry.PerpendicularityToleranceDeg)
            { error = $"Branch is not perpendicular to the main (angle={angleDeg:F1}°, required 89°–91°)."; return null; }

            return doc.Create.NewTeeFitting(mainConnector, mainConnector, branchConn);
        }

        private static Connector PickBranchConnector(MEPCurve element, int? index, Curve mainCurve, out string? error)
        {
            error = null;
            if (index.HasValue)
                return IndexConnector(element, index.Value, out error)!;
            return MepCurveGeometry.FindClosestConnectorToCurve(element, mainCurve)!;
        }

        private static Connector IndexConnector(MEPCurve element, int index, out string? error)
        {
            error = null;
            var list = new List<Connector>();
            foreach (Connector c in element.ConnectorManager.Connectors)
                list.Add(c);
            if (index < 0 || index >= list.Count)
            { error = $"connector index {index} is out of range (0–{list.Count - 1})."; return null!; }
            return list[index];
        }

        private static MEPCurve? ResolveElement(Document doc, int id, string cls, out string? label)
        {
            label = null;
            var e = doc.GetElement(new ElementId(id));
            if (e is not MEPCurve mc) { label = $"Element {id}"; return null; }

            var expectedCat = cls == "duct" ? BuiltInCategory.OST_DuctCurves
                : cls == "pipe" ? BuiltInCategory.OST_PipeCurves
                : BuiltInCategory.OST_CableTray;
            if (mc.Category?.Id.IntegerValue != (int)expectedCat) { label = $"Element {id}"; return null; }
            return mc;
        }

        private static (double DiameterMm, double WidthMm, double HeightMm) ReadSize(MEPCurve e)
        {
            double dia = e.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.AsDouble().FeetToMillimeter()
                         ?? e.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble().FeetToMillimeter() ?? 0.0;
            double w = e.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble().FeetToMillimeter()
                       ?? e.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble().FeetToMillimeter() ?? 0.0;
            double h = e.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble().FeetToMillimeter()
                       ?? e.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble().FeetToMillimeter() ?? 0.0;
            return (dia, w, h);
        }
    }

    public class MepFittingParams
    {
        [Param("type", Required = true)]
        public string Type { get; set; } = "";

        [Param("class", Required = true)]
        public string Class { get; set; } = "";

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