using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Creation
{
    public class CreateDimensionsHandler : DocumentCommandBase
    {
        public override string CommandName => "create_dimensions";
        public override string Description => "Creates a linear dimension across two or more grid lines in a view (grids only; multi-segment when more than two grids are given)";
        public override string Category => "Create";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_ids", Type = "int[]", Required = true, Description = "Two or more Grid element IDs, in the order they appear on the dimension (grids should be parallel)" },
            new CommandParamSchema { Name = "view_id", Type = "int", Required = false, Description = "View element ID (defaults to active view)" },
            new CommandParamSchema { Name = "offset_mm", Type = "double", Required = false, Description = "Shifts the auto dimension line along the grid direction (used when explicit line points are omitted)", Default = 0 },
            new CommandParamSchema { Name = "line_start_x", Type = "double", Required = false, Description = "Dimension line start X in millimeters (explicit mode; overrides offset_mm)" },
            new CommandParamSchema { Name = "line_start_y", Type = "double", Required = false, Description = "Dimension line start Y in millimeters (explicit mode)" },
            new CommandParamSchema { Name = "line_end_x", Type = "double", Required = false, Description = "Dimension line end X in millimeters (explicit mode)" },
            new CommandParamSchema { Name = "line_end_y", Type = "double", Required = false, Description = "Dimension line end Y in millimeters (explicit mode)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_dimensions\", \"parameters\": { \"element_ids\": [123, 456] } }",
            "{ \"command\": \"create_dimensions\", \"parameters\": { \"element_ids\": [123, 456, 789], \"offset_mm\": 2000 } }",
            "{ \"command\": \"create_dimensions\", \"parameters\": { \"element_ids\": [123, 456], \"line_start_x\": 0, \"line_start_y\": -1500, \"line_end_x\": 10000, \"line_end_y\": -1500 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var elementIds = HandlerUtilities.GetIntArrayOrNull(parameters, "element_ids");
            if (elementIds is null || elementIds.Length < 2)
                return CommandResponse.Error(cmd.TaskId, "Provide at least two element_ids referencing grid lines.").ToJson();

            var grids = new List<Grid>();
            foreach (var id in elementIds)
            {
                var grid = doc.GetElement(new ElementId(id)) as Grid;
                if (grid is null)
                    return CommandResponse.Error(cmd.TaskId, $"Element with ID {id} is not a Grid element.").ToJson();
                grids.Add(grid);
            }

            var view = ResolveView(doc, parameters);
            if (view is null)
                return CommandResponse.Error(cmd.TaskId, "No active view or specified view not found.").ToJson();

            double offsetMm = HandlerUtilities.GetDoubleOrNull(parameters, "offset_mm") ?? 0.0;
            double? startX = HandlerUtilities.GetDoubleOrNull(parameters, "line_start_x");
            double? startY = HandlerUtilities.GetDoubleOrNull(parameters, "line_start_y");
            double? endX = HandlerUtilities.GetDoubleOrNull(parameters, "line_end_x");
            double? endY = HandlerUtilities.GetDoubleOrNull(parameters, "line_end_y");
            bool explicitLine = startX.HasValue && startY.HasValue && endX.HasValue && endY.HasValue;

            // Closest point pair between the first and last grid curves so the
            // dimension line spans across all intermediate grids (model coords, feet).
            var curve1 = grids.First().Curve;
            var curve2 = grids.Last().Curve;
            XYZ bestA = curve1.Evaluate(0.5, true);
            XYZ bestB = curve2.Evaluate(0.5, true);
            double bestDist = double.MaxValue;

            for (int i = 0; i <= 100; i++)
            {
                var p = curve1.Evaluate((double)i / 100, true);
                var proj = curve2.Project(p);
                if (proj.Distance < bestDist)
                {
                    bestDist = proj.Distance;
                    bestA = p;
                    bestB = proj.XYZPoint;
                }
            }

            Line dimLine;
            if (explicitLine)
            {
                var z = bestA.Z;
                dimLine = Line.CreateBound(
                    new XYZ(startX.Value.MillimeterToFeet(), startY.Value.MillimeterToFeet(), z),
                    new XYZ(endX.Value.MillimeterToFeet(), endY.Value.MillimeterToFeet(), z));
            }
            else
            {
                // A->B is perpendicular to two parallel grids; offset shifts the
                // dimension line along the grid direction.
                var shift = XYZ.Zero;
                if (Math.Abs(offsetMm) > 1e-9)
                {
                    var tangent = curve1.ComputeDerivatives(0.5, true).BasisX.Normalize();
                    shift = tangent * offsetMm.MillimeterToFeet();
                }
                dimLine = Line.CreateBound(bestA + shift, bestB + shift);
            }

            Dimension dimension;

            using (var t = new DryRunTransaction(doc, "CLI Create Dimensions", cmd.DryRun))
            {
                t.ConfigureFailureHandling();

                try
                {
                    var references = new ReferenceArray();
                    foreach (var grid in grids)
                        references.Append(new Reference(grid));

                    dimension = doc.Create.NewDimension(view, dimLine, references);
                }
                catch (ArgumentException)
                {
                    // Dispose rolls back the uncommitted transaction.
                    return CommandResponse.Error(cmd.TaskId,
                        "The grid references could not be used for a dimension in this view. Use a plan view where all grids are visible and parallel.").ToJson();
                }
                catch (Exception ex)
                {
                    return CommandResponse.Error(cmd.TaskId, $"Failed to create dimension: {ex.Message}").ToJson();
                }

                t.Commit();
            }

            var result = new
            {
                dimension_id = dimension.Id.IntegerValue,
                view_id = view.Id.IntegerValue,
                line = new
                {
                    start_x = dimLine.GetEndPoint(0).X.FeetToMillimeter(),
                    start_y = dimLine.GetEndPoint(0).Y.FeetToMillimeter(),
                    end_x = dimLine.GetEndPoint(1).X.FeetToMillimeter(),
                    end_y = dimLine.GetEndPoint(1).Y.FeetToMillimeter()
                },
                element_ids = elementIds
            };

            return CommandResponse.Success(cmd.TaskId, result, "Dimension created successfully.").ToJson();
        }

        private View? ResolveView(Document doc, Dictionary<string, object> parameters)
        {
            int? viewId = HandlerUtilities.GetIntOrNull(parameters, "view_id");
            if (viewId is not null)
            {
                var viewElem = doc.GetElement(new ElementId(viewId.Value));
                if (viewElem is View v && !v.IsTemplate)
                    return v;
                return null;
            }

            return doc.ActiveView;
        }
    }
}
