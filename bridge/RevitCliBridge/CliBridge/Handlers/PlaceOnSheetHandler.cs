using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    public class PlaceOnSheetHandler : DocumentCommandBase
    {
        public override string CommandName => "place_on_sheet";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? viewId = HandlerUtilities.GetIntOrNull(parameters, "view_id");
            int? sheetId = HandlerUtilities.GetIntOrNull(parameters, "sheet_id");
            double? x = HandlerUtilities.GetDoubleOrNull(parameters, "x");
            double? y = HandlerUtilities.GetDoubleOrNull(parameters, "y");
            int? viewportTypeId = HandlerUtilities.GetIntOrNull(parameters, "viewport_type_id");

            if (!viewId.HasValue)
                return CommandResponse.Error(cmd.TaskId, "Missing 'view_id' parameter.").ToJson();

            if (!sheetId.HasValue)
                return CommandResponse.Error(cmd.TaskId, "Missing 'sheet_id' parameter.").ToJson();

            var view = doc.GetElement(new ElementId(viewId.Value)) as View;
            if (view is null)
                return CommandResponse.Error(cmd.TaskId, $"View with ID {viewId.Value} not found.").ToJson();

            var sheet = doc.GetElement(new ElementId(sheetId.Value)) as ViewSheet;
            if (sheet is null)
                return CommandResponse.Error(cmd.TaskId, $"Sheet with ID {sheetId.Value} not found.").ToJson();

            XYZ location;
            if (x.HasValue && y.HasValue)
            {
                location = new XYZ(x.Value.MillimeterToFeet(), y.Value.MillimeterToFeet(), 0);
            }
            else
            {
                location = sheet.Origin;
            }

            Viewport? viewport = null;

            using (Transaction t = new Transaction(doc, "CLI Place View on Sheet"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                try
                {
                    if (viewportTypeId.HasValue && viewportTypeId.Value > 0)
                    {
                        var vpType = doc.GetElement(new ElementId(viewportTypeId.Value)) as ElementType;
                        if (vpType is not null)
                            viewport = Viewport.Create(doc, sheet.Id, view.Id, location);
                        else
                            viewport = Viewport.Create(doc, sheet.Id, view.Id, location);
                    }
                    else
                    {
                        viewport = Viewport.Create(doc, sheet.Id, view.Id, location);
                    }
                }
                catch (Exception ex)
                {
                    return CommandResponse.Error(cmd.TaskId, $"Failed to place view on sheet: {ex.Message}").ToJson();
                }

                t.Commit();
            }

            if (viewport is null)
                return CommandResponse.Error(cmd.TaskId, "Failed to create viewport.").ToJson();

            var result = new
            {
                viewport_id = viewport.Id.IntegerValue,
                view_id = viewId.Value,
                sheet_id = sheetId.Value,
                location_x = location.X.FeetToMillimeter(),
                location_y = location.Y.FeetToMillimeter()
            };

            return CommandResponse.Success(cmd.TaskId, result, "View placed on sheet successfully.").ToJson();
        }
    }
}
