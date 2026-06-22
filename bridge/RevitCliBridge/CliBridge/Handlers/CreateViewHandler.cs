using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class CreateViewHandler : DocumentCommandBase
    {
        public override string CommandName => "create_view";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            string? viewType = HandlerUtilities.GetStringOrNull(parameters, "type")?.ToLower();
            string? viewName = HandlerUtilities.GetStringOrNull(parameters, "name");
            int? templateId = HandlerUtilities.GetIntOrNull(parameters, "template_id");

            if (string.IsNullOrEmpty(viewType))
                return CommandResponse.Error(cmd.TaskId, "Missing 'type' parameter. Supported: plan, section, ceiling_plan, structural_plan, area_plan, 3d.").ToJson();

            View? newView = null;

            using (Transaction t = new Transaction(doc, "CLI Create View"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                try
                {
                    switch (viewType)
                    {
                        case "plan":
                        case "floorplan":
                            newView = CreateFloorPlanView(doc, parameters);
                            break;

                        case "ceiling_plan":
                        case "ceilingplan":
                            newView = CreateCeilingPlanView(doc, parameters);
                            break;

                        case "structural_plan":
                        case "structuralplan":
                            newView = CreateStructuralPlanView(doc, parameters);
                            break;

                        case "area_plan":
                        case "areaplan":
                            newView = CreateAreaPlanView(doc, parameters);
                            break;

                        case "section":
                            newView = CreateSectionView(doc, parameters);
                            break;

                        case "3d":
                            newView = Create3DView(doc);
                            break;

                        default:
                            return CommandResponse.Error(cmd.TaskId,
                                $"Unsupported view type: '{viewType}'. Supported: plan, ceiling_plan, structural_plan, area_plan, section, 3d.").ToJson();
                    }
                }
                catch (Exception ex)
                {
                    return CommandResponse.Error(cmd.TaskId, $"Failed to create view: {ex.Message}").ToJson();
                }

                if (newView is null)
                    return CommandResponse.Error(cmd.TaskId, "Failed to create view. Check level_id and view type.").ToJson();

                if (!string.IsNullOrEmpty(viewName))
                {
                    try
                    {
                        newView.Name = viewName;
                    }
                    catch
                    {
                    }
                }

                if (templateId.HasValue && templateId.Value > 0)
                {
                    var tplElement = doc.GetElement(new ElementId(templateId.Value));
                    if (tplElement is View tplView && tplView.IsTemplate)
                    {
                        newView.ViewTemplateId = tplView.Id;
                    }
                }

                t.Commit();
            }

            var result = new
            {
                element_id = newView.Id.IntegerValue,
                name = newView.Name,
                view_type = newView.ViewType.ToString()
            };

            return CommandResponse.Success(cmd.TaskId, result, "View created successfully.").ToJson();
        }

        private View? CreateFloorPlanView(Document doc, Dictionary<string, object> parameters)
        {
            int? levelId = HandlerUtilities.GetIntOrNull(parameters, "level_id");
            if (!levelId.HasValue)
                return null;

            var level = doc.GetElement(new ElementId(levelId.Value)) as Level;
            if (level is null)
                return null;

            return ViewPlan.Create(doc, level.Id, doc.ActiveView?.Id ?? ElementId.InvalidElementId);
        }

        private View? CreateCeilingPlanView(Document doc, Dictionary<string, object> parameters)
        {
            int? levelId = HandlerUtilities.GetIntOrNull(parameters, "level_id");
            if (!levelId.HasValue)
                return null;

            var level = doc.GetElement(new ElementId(levelId.Value)) as Level;
            if (level is null)
                return null;

            var viewFamilyTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(x => x.ViewFamily == ViewFamily.CeilingPlan);

            var viewType = viewFamilyTypes.FirstOrDefault();
            if (viewType is null)
                return null;

            return ViewPlan.Create(doc, level.Id, viewType.Id);
        }

        private View? CreateStructuralPlanView(Document doc, Dictionary<string, object> parameters)
        {
            int? levelId = HandlerUtilities.GetIntOrNull(parameters, "level_id");
            if (!levelId.HasValue)
                return null;

            var level = doc.GetElement(new ElementId(levelId.Value)) as Level;
            if (level is null)
                return null;

            var viewFamilyTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(x => x.ViewFamily == ViewFamily.StructuralPlan);

            var viewType = viewFamilyTypes.FirstOrDefault();
            if (viewType is null)
                return null;

            return ViewPlan.Create(doc, level.Id, viewType.Id);
        }

        private View? CreateAreaPlanView(Document doc, Dictionary<string, object> parameters)
        {
            int? levelId = HandlerUtilities.GetIntOrNull(parameters, "level_id");
            if (!levelId.HasValue)
                return null;

            var level = doc.GetElement(new ElementId(levelId.Value)) as Level;
            if (level is null)
                return null;

            var viewFamilyTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(x => x.ViewFamily == ViewFamily.AreaPlan);

            var viewType = viewFamilyTypes.FirstOrDefault();
            if (viewType is null)
                return null;

            return ViewPlan.Create(doc, level.Id, viewType.Id);
        }

        private View? CreateSectionView(Document doc, Dictionary<string, object> parameters)
        {
            int? levelId = HandlerUtilities.GetIntOrNull(parameters, "level_id");
            if (!levelId.HasValue)
                return null;

            var level = doc.GetElement(new ElementId(levelId.Value)) as Level;
            if (level is null)
                return null;

            double elevation = level.Elevation;
            var bb = doc.ActiveView?.get_BoundingBox(null);
            double minX = bb is not null ? bb.Min.X : 0;
            double maxX = bb is not null ? bb.Max.X : 100;
            double minY = bb is not null ? bb.Min.Y : 0;
            double maxY = bb is not null ? bb.Max.Y : 100;

            var line = Line.CreateBound(
                new XYZ(minX, minY, elevation),
                new XYZ(maxX, maxY, elevation));

            var box = new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, elevation - 10),
                Max = new XYZ(maxX, maxY, elevation + 30)
            };

            var viewFamilyTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(x => x.ViewFamily == ViewFamily.Section);

            var viewType = viewFamilyTypes.FirstOrDefault();
            if (viewType is null)
                return null;

            return ViewSection.CreateSection(doc, viewType.Id, box);
        }

        private View Create3DView(Document doc)
        {
            var viewFamilyTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(x => x.ViewFamily == ViewFamily.ThreeDimensional);

            var viewType = viewFamilyTypes.FirstOrDefault();
            if (viewType is null)
                throw new InvalidOperationException("No 3D view family type found.");

            return View3D.CreateIsometric(doc, viewType.Id);
        }
    }
}
