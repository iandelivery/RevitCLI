using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Views
{
    public class CheckSheetBoundsHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["x"] = UnitKinds.Length,
                ["y"] = UnitKinds.Length,
                ["min_x"] = UnitKinds.Length,
                ["min_y"] = UnitKinds.Length,
                ["max_x"] = UnitKinds.Length,
                ["max_y"] = UnitKinds.Length,
                ["left"] = UnitKinds.Length,
                ["right"] = UnitKinds.Length,
                ["top"] = UnitKinds.Length,
                ["bottom"] = UnitKinds.Length
            };

        public override string CommandName => "check_sheet_bounds";
        public override string Description => "Reports whether each viewport on a sheet sits inside the drawable area of the title block";
        public override string Category => "Query";
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "sheet_id", Type = "int", Required = true, Description = "Sheet element ID" },
            new CommandParamSchema { Name = "titleblock_id", Type = "int", Required = false, Description = "Title block instance on the sheet (defaults to the sheet's single title block)" },
            new CommandParamSchema { Name = "viewport_ids", Type = "int[]", Required = false, Description = "Viewport element IDs to check (defaults to every viewport on the sheet)" },
            new CommandParamSchema { Name = "margin", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Uniform inset applied to all four edges of the title block box (request length unit, default mm; default 0)", Default = 0 },
            new CommandParamSchema { Name = "margin_left", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Left inset (request length unit, default mm; defaults to margin)" },
            new CommandParamSchema { Name = "margin_right", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Right inset (request length unit, default mm; defaults to margin)" },
            new CommandParamSchema { Name = "margin_top", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Top inset (request length unit, default mm; defaults to margin)" },
            new CommandParamSchema { Name = "margin_bottom", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Bottom inset (request length unit, default mm; defaults to margin)" },
            new CommandParamSchema { Name = "include_label", Type = "bool", Required = false, Description = "Also check the viewport label outline (default true)", Default = true }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"check_sheet_bounds\", \"parameters\": { \"sheet_id\": 12345 } }",
            "{ \"command\": \"check_sheet_bounds\", \"parameters\": { \"sheet_id\": 12345, \"margin\": 10 } }",
            "{ \"command\": \"check_sheet_bounds\", \"parameters\": { \"sheet_id\": 12345, \"margin_left\": 5, \"margin_right\": 40, \"include_label\": false } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            int? sheetId = HandlerUtilities.GetIntOrNull(parameters, "sheet_id");
            if (!sheetId.HasValue)
                return CommandResponse.Error(cmd.TaskId, "Missing 'sheet_id' parameter.").ToJson();

            var sheet = SheetViewportHelpers.ResolveSheet(doc, sheetId.Value, out var sheetReason);
            if (sheet is null)
                return CommandResponse.Error(cmd.TaskId, sheetReason!).ToJson();

            var margin = HandlerUtilities.GetDoubleOrNull(parameters, "margin") ?? 0;
            var marginLeft = HandlerUtilities.GetDoubleOrNull(parameters, "margin_left") ?? margin;
            var marginRight = HandlerUtilities.GetDoubleOrNull(parameters, "margin_right") ?? margin;
            var marginTop = HandlerUtilities.GetDoubleOrNull(parameters, "margin_top") ?? margin;
            var marginBottom = HandlerUtilities.GetDoubleOrNull(parameters, "margin_bottom") ?? margin;

            if (margin < 0 || marginLeft < 0 || marginRight < 0 || marginTop < 0 || marginBottom < 0)
                return CommandResponse.Error(cmd.TaskId, "margin must be nonnegative.").ToJson();

            var titleBlock = ResolveTitleBlock(doc, sheet, HandlerUtilities.GetIntOrNull(parameters, "titleblock_id"), out var tbError);
            if (titleBlock is null)
                return CommandResponse.Error(cmd.TaskId, tbError!).ToJson();

            var bbox = titleBlock.get_BoundingBox(sheet);
            if (bbox is null)
                return CommandResponse.Error(cmd.TaskId,
                    $"Title block {titleBlock.Id.IntegerValue} has no geometry to derive bounds from.").ToJson();

            var region = SheetBoundsCalculator.Shrink(
                SheetViewportHelpers.ToRect(bbox.Min, bbox.Max),
                marginLeft.FeetToMillimeter(), marginRight.FeetToMillimeter(),
                marginTop.FeetToMillimeter(), marginBottom.FeetToMillimeter());

            var viewports = SheetViewportHelpers.GetViewports(doc, sheet);
            var filterError = FilterViewports(parameters, viewports, out var selected);
            if (filterError is not null)
                return CommandResponse.Error(cmd.TaskId, filterError).ToJson();

            var includeLabel = true;
            if (parameters.TryGetValue("include_label", out var rawLabel)
                && !ParamConverter.TryConvertBool(rawLabel, out includeLabel))
                return CommandResponse.Error(cmd.TaskId, "'include_label' must be a boolean.").ToJson();

            var entries = new List<object>();
            var outOfBoundsCount = 0;

            foreach (var viewport in selected)
            {
                var view = (View?)doc.GetElement(viewport.ViewId);
                var box = SheetViewportHelpers.BoxRect(viewport);
                var label = includeLabel ? SheetViewportHelpers.LabelRect(viewport) : null;
                var center = viewport.GetBoxCenter();

                var boxInBounds = SheetBoundsCalculator.Contains(region, box);
                var labelInBounds = label is null || SheetBoundsCalculator.Contains(region, label!.Value);
                var overflow = SheetBoundsCalculator.OverflowAgainst(region, box);

                if (!boxInBounds || !labelInBounds)
                    outOfBoundsCount++;

                entries.Add(new
                {
                    viewport_id = viewport.Id.IntegerValue,
                    view_id = viewport.ViewId.IntegerValue,
                    view_name = view?.Name,
                    center = new { x = center.X, y = center.Y },
                    box = SheetViewportHelpers.RectJsonToInternal(box),
                    label = label.HasValue ? SheetViewportHelpers.RectJsonToInternal(label!.Value) : null,
                    box_in_bounds = boxInBounds,
                    label_in_bounds = labelInBounds,
                    overflow = new
                    {
                        left = overflow.Left.MillimeterToFeet(),
                        right = overflow.Right.MillimeterToFeet(),
                        top = overflow.Top.MillimeterToFeet(),
                        bottom = overflow.Bottom.MillimeterToFeet()
                    }
                });
            }

            var result = new
            {
                sheet_id = sheetId.Value,
                sheet_number = sheet.SheetNumber,
                title_block_id = titleBlock.Id.IntegerValue,
                allowed_region = SheetViewportHelpers.RectJsonToInternal(region),
                count = entries.Count,
                viewports = entries,
                out_of_bounds_count = outOfBoundsCount,
                all_in_bounds = outOfBoundsCount == 0
            };

            return CommandResponse.Success(cmd.TaskId, result,
                outOfBoundsCount == 0
                    ? $"All {entries.Count} viewports sit inside the title block drawable area."
                    : $"{outOfBoundsCount} of {entries.Count} viewports cross the title block drawable area.").ToJson();
        }

        private static FamilyInstance? ResolveTitleBlock(Document doc, ViewSheet sheet, int? titleblockId, out string? error)
        {
            error = null;

            if (titleblockId.HasValue)
            {
                var titleBlockCategoryId = new ElementId(BuiltInCategory.OST_TitleBlocks);
                var instance = doc.GetElement(new ElementId(titleblockId.Value)) as FamilyInstance;
                if (instance is null
                    || instance.Category is null
                    || instance.Category.Id != titleBlockCategoryId
                    || instance.OwnerViewId != sheet.Id)
                {
                    error = $"Element with ID {titleblockId.Value} is not a title block on sheet {sheet.SheetNumber}.";
                    return null;
                }
                return instance;
            }

            var candidates = SheetViewportHelpers.GetTitleBlocks(doc, sheet);
            if (candidates.Count == 0)
            {
                error = $"Sheet {sheet.SheetNumber} has no title block.";
                return null;
            }

            if (candidates.Count > 1)
            {
                var ids = string.Join(", ", candidates.Select(tb => tb.Id.IntegerValue));
                error = $"Sheet {sheet.SheetNumber} has {candidates.Count} title blocks: pass titleblock_id. Candidates: {ids}.";
                return null;
            }

            return candidates[0];
        }

        private static string? FilterViewports(Dictionary<string, object> parameters, List<Viewport> viewports, out List<Viewport> selected)
        {
            selected = viewports;
            var ids = HandlerUtilities.GetIntArrayOrNull(parameters, "viewport_ids");
            if (ids is null || ids.Length == 0)
                return null;

            var byId = viewports.ToDictionary(vp => vp.Id.IntegerValue);
            foreach (var id in ids.Distinct())
            {
                if (!byId.ContainsKey(id))
                    return $"Viewport {id} is not on this sheet.";
            }

            selected = ids.Distinct().Select(id => byId[id]).ToList();
            return null;
        }
    }
}
