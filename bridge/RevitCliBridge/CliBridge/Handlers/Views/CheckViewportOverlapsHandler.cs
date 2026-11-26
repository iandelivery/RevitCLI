using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Views
{
    public class CheckViewportOverlapsHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["min_x"] = UnitKinds.Length,
                ["min_y"] = UnitKinds.Length,
                ["max_x"] = UnitKinds.Length,
                ["max_y"] = UnitKinds.Length,
                ["overlap_area"] = UnitKinds.Area
            };

        public override string CommandName => "check_viewport_overlaps";
        public override string Description => "Reports which viewports on a sheet overlap each other, with the overlap rectangle and area in the request units; box overlap does not prove the drawn contents overlap";
        public override string Category => "Query";
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "sheet_id", Type = "int", Required = true, Description = "Sheet element ID" },
            new CommandParamSchema { Name = "viewport_ids", Type = "int[]", Required = false, Description = "Viewport element IDs to compare (defaults to every viewport on the sheet)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"check_viewport_overlaps\", \"parameters\": { \"sheet_id\": 12345 } }",
            "{ \"command\": \"check_viewport_overlaps\", \"parameters\": { \"sheet_id\": 12345, \"viewport_ids\": [135790, 135791] } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            int? sheetId = HandlerUtilities.GetIntOrNull(parameters, "sheet_id");
            if (!sheetId.HasValue)
                return CommandResponse.Error(cmd.TaskId, "Missing 'sheet_id' parameter.").ToJson();

            var sheet = SheetViewportHelpers.ResolveSheet(doc, sheetId.Value, out var sheetReason);
            if (sheet is null)
                return CommandResponse.Error(cmd.TaskId, sheetReason!).ToJson();

            var viewports = SheetViewportHelpers.GetViewports(doc, sheet);

            var ids = HandlerUtilities.GetIntArrayOrNull(parameters, "viewport_ids");
            if (ids is not null && ids.Length > 0)
            {
                var byId = viewports.ToDictionary(vp => ElementIdCompat.ValueOf(vp.Id));
                foreach (var id in ids.Distinct())
                {
                    if (!byId.ContainsKey(id))
                        return CommandResponse.Error(cmd.TaskId, $"Viewport {id} is not on this sheet.").ToJson();
                }
                viewports = ids.Distinct().Select(id => byId[id]).ToList();
            }

            var boxes = viewports
                .Select(vp => new { Viewport = vp, Box = SheetViewportHelpers.BoxRect(vp) })
                .ToList();

            var overlaps = new List<object>();
            for (var i = 0; i < boxes.Count; i++)
            {
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var intersection = SheetBoundsCalculator.Intersect(boxes[i].Box, boxes[j].Box);
                    if (intersection is null)
                        continue;

                    var viewA = (View?)doc.GetElement(boxes[i].Viewport.ViewId);
                    var viewB = (View?)doc.GetElement(boxes[j].Viewport.ViewId);

                    overlaps.Add(new
                    {
                        viewport_a = ElementIdCompat.ValueOf(boxes[i].Viewport.Id),
                        viewport_b = ElementIdCompat.ValueOf(boxes[j].Viewport.Id),
                        view_name_a = viewA?.Name,
                        view_name_b = viewB?.Name,
                        overlap = SheetViewportHelpers.RectJsonToInternal(intersection.Value),
                        overlap_area = UnitContracts.ToInternal(UnitKinds.Area, "mm2",
                            SheetBoundsCalculator.Area(intersection.Value))
                    });
                }
            }

            var result = new
            {
                sheet_id = sheetId.Value,
                sheet_number = sheet.SheetNumber,
                viewport_count = boxes.Count,
                overlap_count = overlaps.Count,
                has_overlaps = overlaps.Count > 0,
                overlaps
            };

            var pairNoun = overlaps.Count == 1 ? "pair" : "pairs";
            var overlapVerb = overlaps.Count == 1 ? "overlaps" : "overlap";

            return CommandResponse.Success(cmd.TaskId, result,
                overlaps.Count == 0
                    ? $"No viewport boxes overlap on sheet {sheet.SheetNumber}."
                    : $"{overlaps.Count} viewport {pairNoun} {overlapVerb} on sheet {sheet.SheetNumber}.").ToJson();
        }
    }
}
