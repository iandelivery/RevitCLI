using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Views
{
    public class SetSectionBoxHandler : DocumentCommandBase
    {
        public override string CommandName => "set_section_box";
        public override string Description => "Sets or toggles the section box on a 3D view";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "view_id", Type = "int", Required = false, Description = "3D view element ID (defaults to the active view)" },
            new CommandParamSchema { Name = "enable", Type = "bool", Required = false, Description = "Enable or disable the section box (defaults to true)", Default = true },
            new CommandParamSchema { Name = "min_x", Type = "double", Required = false, Description = "Minimum X coordinate of the section box in millimeters (required when enable is true)" },
            new CommandParamSchema { Name = "min_y", Type = "double", Required = false, Description = "Minimum Y coordinate of the section box in millimeters (required when enable is true)" },
            new CommandParamSchema { Name = "min_z", Type = "double", Required = false, Description = "Minimum Z coordinate of the section box in millimeters (required when enable is true)" },
            new CommandParamSchema { Name = "max_x", Type = "double", Required = false, Description = "Maximum X coordinate of the section box in millimeters (required when enable is true)" },
            new CommandParamSchema { Name = "max_y", Type = "double", Required = false, Description = "Maximum Y coordinate of the section box in millimeters (required when enable is true)" },
            new CommandParamSchema { Name = "max_z", Type = "double", Required = false, Description = "Maximum Z coordinate of the section box in millimeters (required when enable is true)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"set_section_box\", \"parameters\": { \"view_id\": 12345, \"min_x\": 0, \"min_y\": 0, \"min_z\": 0, \"max_x\": 10000, \"max_y\": 8000, \"max_z\": 4000 } }",
            "{ \"command\": \"set_section_box\", \"parameters\": { \"view_id\": 12345, \"enable\": false } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<SetSectionBoxParams>(cmd, out var error);
            if (p is null) return error!;

            // Resolve the target 3D view: explicit view_id, else active view.
            View3D? view = null;
            if (p.ViewId.HasValue)
            {
                view = doc.GetElement(new ElementId(p.ViewId.Value)) as View3D;
                if (view is null)
                    return CommandResponse.Error(cmd.TaskId, $"Element with ID {p.ViewId.Value} is not a 3D view.").ToJson();
            }
            else
            {
                var uiDoc = app.ActiveUIDocument;
                var activeView = uiDoc?.ActiveView;
                if (activeView is null)
                    return CommandResponse.Error(cmd.TaskId, "No active view and no view_id provided.").ToJson();
                view = activeView as View3D;
                if (view is null)
                    return CommandResponse.Error(cmd.TaskId, "Active view is not a 3D view. Provide view_id of a 3D view instead.").ToJson();
            }

            if (view.IsTemplate)
                return CommandResponse.Error(cmd.TaskId, "Cannot set a section box on a view template.").ToJson();

            if (view.IsPerspective)
                return CommandResponse.Error(cmd.TaskId, "Perspective views do not support section boxes.").ToJson();

            if (!p.Enable)
            {
                using (var t = new DryRunTransaction(doc, "CLI Set Section Box", cmd.DryRun))
                {
                    t.ConfigureFailureHandling();
                    view.IsSectionBoxActive = false;
                    t.Commit();
                }

                var disabledResult = new
                {
                    view_id = view.Id.IntegerValue,
                    view_name = view.Name,
                    active = false
                };
                return CommandResponse.Success(cmd.TaskId, disabledResult, "Section box disabled.").ToJson();
            }

            // All six bounds are required when enabling the section box.
            if (!p.MinX.HasValue || !p.MinY.HasValue || !p.MinZ.HasValue ||
                !p.MaxX.HasValue || !p.MaxY.HasValue || !p.MaxZ.HasValue)
            {
                return CommandResponse.Error(cmd.TaskId,
                    "Missing bounds: min_x, min_y, min_z, max_x, max_y, max_z are required when enable is true.").ToJson();
            }

            if (p.MinX.Value >= p.MaxX.Value || p.MinY.Value >= p.MaxY.Value || p.MinZ.Value >= p.MaxZ.Value)
                return CommandResponse.Error(cmd.TaskId,
                    "Invalid bounds: each min coordinate must be less than the corresponding max coordinate.").ToJson();

            var bounds = new BoundingBoxXYZ
            {
                Min = new XYZ(p.MinX.Value.MillimeterToFeet(), p.MinY.Value.MillimeterToFeet(), p.MinZ.Value.MillimeterToFeet()),
                Max = new XYZ(p.MaxX.Value.MillimeterToFeet(), p.MaxY.Value.MillimeterToFeet(), p.MaxZ.Value.MillimeterToFeet())
            };

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
                min_x = p.MinX,
                min_y = p.MinY,
                min_z = p.MinZ,
                max_x = p.MaxX,
                max_y = p.MaxY,
                max_z = p.MaxZ
            };
            return CommandResponse.Success(cmd.TaskId, result, "Section box set successfully.").ToJson();
        }
    }

    /// <summary>
    /// Typed parameter bag for <see cref="SetSectionBoxHandler"/>.
    /// Bounds are nullable so "enable": false can omit them entirely.
    /// </summary>
    public class SetSectionBoxParams
    {
        [Param("view_id")]
        public int? ViewId { get; set; }

        [Param("enable", Default = true)]
        public bool Enable { get; set; }

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
}
