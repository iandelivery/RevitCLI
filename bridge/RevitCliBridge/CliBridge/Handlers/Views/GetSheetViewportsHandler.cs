using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers.Views
{
    public class GetSheetViewportsHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["x"] = UnitKinds.Length,
                ["y"] = UnitKinds.Length,
                ["min_x"] = UnitKinds.Length,
                ["min_y"] = UnitKinds.Length,
                ["max_x"] = UnitKinds.Length,
                ["max_y"] = UnitKinds.Length
            };

        public override string CommandName => "get_sheet_viewports";
        public override string Description => "Lists the viewports placed on a sheet with their box and label outlines in the request length unit (default mm)";
        public override string Category => "Query";
        public override string[] Aliases => new[] { "list_sheet_viewports" };
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "sheet_id", Type = "int", Required = true, Description = "Sheet element ID" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_sheet_viewports\", \"parameters\": { \"sheet_id\": 12345 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            int? sheetId = HandlerUtilities.GetIntOrNull(parameters, "sheet_id");
            if (!sheetId.HasValue)
                return CommandResponse.Error(cmd.TaskId, "Missing 'sheet_id' parameter.").ToJson();

            var sheet = SheetViewportHelpers.ResolveSheet(doc, sheetId.Value, out var sheetReason);
            if (sheet is null)
                return CommandResponse.Error(cmd.TaskId, sheetReason!).ToJson();

            var viewports = new List<object>();
            foreach (var id in sheet.GetAllViewports())
            {
                if (doc.GetElement(id) is not Viewport viewport)
                    continue;

                var view = (View?)doc.GetElement(viewport.ViewId);
                var center = viewport.GetBoxCenter();
                var label = SheetViewportHelpers.LabelRect(viewport);

                viewports.Add(new
                {
                    viewport_id = ElementIdCompat.ValueOf(viewport.Id),
                    view_id = ElementIdCompat.ValueOf(viewport.ViewId),
                    view_name = view?.Name,
                    view_type = view?.ViewType.ToString(),
                    pinned = viewport.Pinned,
                    center = new { x = center.X, y = center.Y },
                    box = SheetViewportHelpers.RectJsonToInternal(SheetViewportHelpers.BoxRect(viewport)),
                    label = label.HasValue ? SheetViewportHelpers.RectJsonToInternal(label!.Value) : null
                });
            }

            var result = new
            {
                sheet_id = sheetId.Value,
                sheet_number = sheet.SheetNumber,
                count = viewports.Count,
                viewports
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Retrieved {viewports.Count} viewports from sheet {sheet.SheetNumber}.").ToJson();
        }
    }
}
