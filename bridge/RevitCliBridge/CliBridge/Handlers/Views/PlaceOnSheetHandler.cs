using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers.Views
{
    public class PlaceOnSheetHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["location_x"] = UnitKinds.Length,
                ["location_y"] = UnitKinds.Length
            };

        public override string CommandName => "place_on_sheet";
        public override string Description => "Places views on a sheet at specified locations";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "sheet_id", Type = "int", Required = true, Description = "Sheet element ID" },
            new CommandParamSchema
            {
                Name = "views", Type = "object", Required = true, UnitKind = UnitKinds.Length, Description = "Array of view placements",
                Properties = new[]
                {
                    new CommandParamSchema { Name = "view_id", Type = "int", Required = true, Description = "View element ID to place" },
                    new CommandParamSchema { Name = "x", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Placement X on sheet (request length unit, default mm; defaults to the sheet center)" },
                    new CommandParamSchema { Name = "y", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Placement Y on sheet (request length unit, default mm; defaults to the sheet center)" }
                }
            }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"place_on_sheet\", \"parameters\": { \"sheet_id\": 12345, \"views\": [ { \"view_id\": 678 } ] } }",
            "{ \"command\": \"place_on_sheet\", \"parameters\": { \"sheet_id\": 12345, \"views\": [ { \"view_id\": 678, \"x\": 100, \"y\": 200 }, { \"view_id\": 789, \"x\": 400, \"y\": 200 } ] } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            int? sheetId = HandlerUtilities.GetIntOrNull(parameters, "sheet_id");
            if (!sheetId.HasValue)
                return CommandResponse.Error(cmd.TaskId, "Missing 'sheet_id' parameter.").ToJson();

            var sheet = doc.GetElement(ElementIdCompat.FromValue(sheetId.Value)) as ViewSheet;
            if (sheet is null)
                return CommandResponse.Error(cmd.TaskId, $"Sheet with ID {sheetId.Value} not found.").ToJson();

            if (!SheetPlacementParser.TryParse(
                    parameters.TryGetValue("views", out var rawViews) ? rawViews : null,
                    "views", "view_id", false, out var items, out var parseError))
                return CommandResponse.Error(cmd.TaskId, parseError!).ToJson();

            var views = new View?[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var view = doc.GetElement(ElementIdCompat.FromValue((long)item.Id)) as View;
                if (view is null)
                    return CommandResponse.Error(cmd.TaskId, $"View with ID {item.Id} not found.").ToJson();
                if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                    return CommandResponse.Error(cmd.TaskId,
                        $"View {item.Id} cannot be placed on sheet {sheet.SheetNumber}. A view can sit on one sheet only, and schedules are placed as ScheduleSheetInstance, not viewports.").ToJson();
                views[i] = view;
            }

            var placed = new List<object>();
            List<string> resolvedFailures;

            using (var t = new DryRunTransaction(doc, "CLI Place Views on Sheet", cmd.DryRun))
            {
                t.ConfigureFailureHandling();

                try
                {
                    for (var i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        var location = item.X.HasValue && item.Y.HasValue
                            ? new XYZ(item.X.Value, item.Y.Value, 0)
                            : sheet.Origin;

                        var viewport = Viewport.Create(doc, sheet.Id, views[i]!.Id, location);
                        placed.Add(new
                        {
                            view_id = item.Id,
                            viewport_id = ElementIdCompat.ValueOf(viewport.Id),
                            location_x = location.X,
                            location_y = location.Y
                        });
                    }

                    doc.Regenerate();
                }
                catch (Exception ex)
                {
                    return CommandResponse.Error(cmd.TaskId, $"Failed to place view on sheet: {ex.Message}").ToJson();
                }

                t.Commit();

                resolvedFailures = t.ResolvedFailures;
            }

            var result = new
            {
                sheet_id = sheetId.Value,
                count = placed.Count,
                placed
            };

            return CommandResponse.SuccessWithResolvedFailures(
                cmd.TaskId, result, $"Placed {placed.Count} views on sheet {sheet.SheetNumber}.", resolvedFailures).ToJson();
        }
    }
}
