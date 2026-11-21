using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Views
{
    /// <summary>
    /// Shared resolution and conversion plumbing for the sheet viewport
    /// commands. Handler-layer code with Revit API dependencies, so unlike
    /// the pure helpers it is not linked into the test project.
    /// </summary>
    internal static class SheetViewportHelpers
    {
        /// <summary>
        /// Resolves a sheet by element ID. Returns null with the failure
        /// reason set when the ID is unknown or not a sheet.
        /// </summary>
        internal static ViewSheet? ResolveSheet(Document doc, int sheetId, out string? reason)
        {
            reason = null;
            var sheet = doc.GetElement(new ElementId(sheetId)) as ViewSheet;
            if (sheet is null)
                reason = $"Sheet with ID {sheetId} not found.";
            return sheet;
        }

        /// <summary>
        /// The viewports placed on a sheet, as Viewport elements.
        /// </summary>
        internal static List<Viewport> GetViewports(Document doc, ViewSheet sheet)
        {
            return sheet.GetAllViewports()
                .Select(id => doc.GetElement(id))
                .OfType<Viewport>()
                .ToList();
        }

        /// <summary>
        /// Converts a sheet-space Outline to a millimeter SheetRect, dropping Z.
        /// </summary>
        internal static SheetRect ToRect(Outline outline)
        {
            return new SheetRect(
                outline.MinimumPoint.X.FeetToMillimeter(),
                outline.MinimumPoint.Y.FeetToMillimeter(),
                outline.MaximumPoint.X.FeetToMillimeter(),
                outline.MaximumPoint.Y.FeetToMillimeter());
        }

        /// <summary>
        /// The viewport box outline in millimeters.
        /// </summary>
        internal static SheetRect BoxRect(Viewport viewport)
        {
            return ToRect(viewport.GetBoxOutline());
        }

        /// <summary>
        /// The viewport label outline in millimeters, or null when the
        /// viewport carries no label, which Revit reports as an outline
        /// whose minimum exceeds its maximum.
        /// </summary>
        internal static SheetRect? LabelRect(Viewport viewport)
        {
            Outline? outline;
            try
            {
                outline = viewport.GetLabelOutline();
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            if (outline is null
                || outline.MinimumPoint.X > outline.MaximumPoint.X
                || outline.MinimumPoint.Y > outline.MaximumPoint.Y)
                return null;

            return ToRect(outline);
        }

        /// <summary>
        /// The rectangle JSON shape shared by the sheet commands.
        /// </summary>
        internal static object RectJson(SheetRect rect)
        {
            return new { min_x = rect.MinX, min_y = rect.MinY, max_x = rect.MaxX, max_y = rect.MaxY };
        }

        /// <summary>
        /// The title block FamilyInstances owned by a sheet.
        /// </summary>
        internal static List<FamilyInstance> GetTitleBlocks(Document doc, ViewSheet sheet)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(tb => tb.OwnerViewId == sheet.Id)
                .ToList();
        }
    }
}
