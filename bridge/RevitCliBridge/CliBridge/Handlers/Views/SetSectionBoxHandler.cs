using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Views
{
    public class SetSectionBoxHandler : DocumentCommandBase
    {
        public override string CommandName => "set_section_box";
        public override string Description => "Sets the section box bounds on a 3D view, either explicitly or computed from elements";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "view_id", Type = "int", Required = false, Description = "3D view element ID (defaults to the active view)" },
            new CommandParamSchema { Name = "element_ids", Type = "int[]", Required = false, ShortFlag = "ids", Description = "Element IDs to compute the section box from (union of their bounding boxes)" },
            new CommandParamSchema { Name = "min_x", Type = "double", Required = false, Description = "Minimum X coordinate of the section box in millimeters (required when element_ids is omitted)" },
            new CommandParamSchema { Name = "min_y", Type = "double", Required = false, Description = "Minimum Y coordinate of the section box in millimeters (required when element_ids is omitted)" },
            new CommandParamSchema { Name = "min_z", Type = "double", Required = false, Description = "Minimum Z coordinate of the section box in millimeters (required when element_ids is omitted)" },
            new CommandParamSchema { Name = "max_x", Type = "double", Required = false, Description = "Maximum X coordinate of the section box in millimeters (required when element_ids is omitted)" },
            new CommandParamSchema { Name = "max_y", Type = "double", Required = false, Description = "Maximum Y coordinate of the section box in millimeters (required when element_ids is omitted)" },
            new CommandParamSchema { Name = "max_z", Type = "double", Required = false, Description = "Maximum Z coordinate of the section box in millimeters (required when element_ids is omitted)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"set_section_box\", \"parameters\": { \"view_id\": 12345, \"min_x\": 0, \"min_y\": 0, \"min_z\": 0, \"max_x\": 10000, \"max_y\": 8000, \"max_z\": 4000 } }",
            "{ \"command\": \"set_section_box\", \"parameters\": { \"element_ids\": [12345, 12346, 12347] } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<SetSectionBoxParams>(cmd, out var error);
            if (p is null) return error!;

            var viewOrError = SectionBoxUtilities.ResolveTargetView(app, doc, p.ViewId, cmd);
            if (viewOrError.View is null) return viewOrError.ErrorJson!;
            var view = viewOrError.View;

            BoundingBoxXYZ? bounds;
            string source;

            if (p.ElementIds is { Length: > 0 })
            {
                bounds = SectionBoxUtilities.ComputeElementsBounds(doc, p.ElementIds, cmd, out var computeError);
                if (bounds is null) return computeError!;
                source = "elements";
            }
            else
            {
                // All six bounds are required when element_ids is omitted.
                if (!p.MinX.HasValue || !p.MinY.HasValue || !p.MinZ.HasValue ||
                    !p.MaxX.HasValue || !p.MaxY.HasValue || !p.MaxZ.HasValue)
                {
                    return CommandResponse.Error(cmd.TaskId,
                        "Provide either element_ids or all six bounds: min_x, min_y, min_z, max_x, max_y, max_z.").ToJson();
                }

                if (p.MinX.Value >= p.MaxX.Value || p.MinY.Value >= p.MaxY.Value || p.MinZ.Value >= p.MaxZ.Value)
                    return CommandResponse.Error(cmd.TaskId,
                        "Invalid bounds: each min coordinate must be less than the corresponding max coordinate.").ToJson();

                bounds = new BoundingBoxXYZ
                {
                    Min = new XYZ(p.MinX.Value.MillimeterToFeet(), p.MinY.Value.MillimeterToFeet(), p.MinZ.Value.MillimeterToFeet()),
                    Max = new XYZ(p.MaxX.Value.MillimeterToFeet(), p.MaxY.Value.MillimeterToFeet(), p.MaxZ.Value.MillimeterToFeet())
                };
                source = "explicit";
            }

            using (var t = new DryRunTransaction(doc, "CLI Set Section Box", cmd.DryRun))
            {
                t.ConfigureFailureHandling();
                view.SetSectionBox(bounds);
                view.IsSectionBoxActive = true;
                t.Commit();
            }

            var result = new
            {
                view_id = view.Id.IntegerValue,
                view_name = view.Name,
                active = true,
                source,
                min_x = bounds.Min.X.FeetToMillimeter(),
                min_y = bounds.Min.Y.FeetToMillimeter(),
                min_z = bounds.Min.Z.FeetToMillimeter(),
                max_x = bounds.Max.X.FeetToMillimeter(),
                max_y = bounds.Max.Y.FeetToMillimeter(),
                max_z = bounds.Max.Z.FeetToMillimeter()
            };
            return CommandResponse.Success(cmd.TaskId, result, "Section box set successfully.").ToJson();
        }
    }

    /// <summary>
    /// Typed parameter bag for <see cref="SetSectionBoxHandler"/>.
    /// Bounds are nullable so the element_ids mode can omit them entirely.
    /// </summary>
    public class SetSectionBoxParams
    {
        [Param("view_id")]
        public int? ViewId { get; set; }

        [Param("element_ids")]
        public int[]? ElementIds { get; set; }

        [Param("min_x")]
        public double? MinX { get; set; }

        [Param("min_y")]
        public double? MinY { get; set; }

        [Param("min_z")]
        public double? MinZ { get; set; }

        [Param("max_x")]
        public double? MaxX { get; set; }

        [Param("max_y")]
        public double? MaxY { get; set; }

        [Param("max_z")]
        public double? MaxZ { get; set; }
    }

    /// <summary>
    /// Shared helpers for the section box commands.
    /// </summary>
    internal static class SectionBoxUtilities
    {
        internal sealed class ViewOrError
        {
            public View3D? View { get; }
            public string? ErrorJson { get; }

            public ViewOrError(View3D? view, string? errorJson)
            {
                View = view;
                ErrorJson = errorJson;
            }
        }

        /// <summary>
        /// Resolves the target 3D view: explicit view_id, else active view.
        /// </summary>
        public static ViewOrError ResolveTargetView(UIApplication app, Document doc, int? viewId, QueuedCommand cmd)
        {
            View3D? view;
            if (viewId.HasValue)
            {
                view = doc.GetElement(new ElementId(viewId.Value)) as View3D;
                if (view is null)
                    return new ViewOrError(null, CommandResponse.Error(cmd.TaskId, $"Element with ID {viewId.Value} is not a 3D view.").ToJson());
            }
            else
            {
                var activeView = app.ActiveUIDocument?.ActiveView;
                if (activeView is null)
                    return new ViewOrError(null, CommandResponse.Error(cmd.TaskId, "No active view and no view_id provided.").ToJson());
                view = activeView as View3D;
                if (view is null)
                    return new ViewOrError(null, CommandResponse.Error(cmd.TaskId, "Active view is not a 3D view. Provide view_id of a 3D view instead.").ToJson());
            }

            if (view.IsTemplate)
                return new ViewOrError(null, CommandResponse.Error(cmd.TaskId, "Cannot set a section box on a view template.").ToJson());

            if (view.IsPerspective)
                return new ViewOrError(null, CommandResponse.Error(cmd.TaskId, "Perspective views do not support section boxes.").ToJson());

            return new ViewOrError(view, null);
        }

        /// <summary>
        /// Computes the union of the bounding boxes of the given elements
        /// (model coordinates, feet). Elements without a bounding box are
        /// skipped; an error is returned when none of them has one.
        /// </summary>
        public static BoundingBoxXYZ? ComputeElementsBounds(Document doc, int[] elementIds, QueuedCommand cmd, out string? errorJson)
        {
            XYZ? min = null;
            XYZ? max = null;
            var skipped = new List<int>();

            foreach (var id in elementIds)
            {
                var element = doc.GetElement(new ElementId(id));
                if (element is null)
                {
                    skipped.Add(id);
                    continue;
                }

                var box = element.get_BoundingBox(null);
                if (box is null)
                {
                    skipped.Add(id);
                    continue;
                }

                min = min is null ? box.Min : new XYZ(
                    System.Math.Min(min.X, box.Min.X),
                    System.Math.Min(min.Y, box.Min.Y),
                    System.Math.Min(min.Z, box.Min.Z));
                max = max is null ? box.Max : new XYZ(
                    System.Math.Max(max.X, box.Max.X),
                    System.Math.Max(max.Y, box.Max.Y),
                    System.Math.Max(max.Z, box.Max.Z));
            }

            if (min is null || max is null)
            {
                errorJson = CommandResponse.Error(cmd.TaskId,
                    $"No bounding boxes found for the given element IDs. Skipped: [{string.Join(", ", skipped)}]").ToJson();
                return null;
            }

            errorJson = null;
            return new BoundingBoxXYZ { Min = min, Max = max };
        }
    }
}
