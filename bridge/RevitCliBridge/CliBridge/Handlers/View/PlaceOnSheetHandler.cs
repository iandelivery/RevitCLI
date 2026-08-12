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
        public override string Description => "Places views on a sheet at specified locations";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "sheet_id", Type = "int", Required = true, Description = "Sheet element ID" },
            new CommandParamSchema
            {
                Name = "views", Type = "object", Required = true, Description = "Array of view placements",
                Properties = new[]
                {
                    new CommandParamSchema { Name = "view_id", Type = "int", Required = true, Description = "View element ID to place" },
                    new CommandParamSchema { Name = "x", Type = "double", Required = false, Description = "Placement X in mm on sheet" },
                    new CommandParamSchema { Name = "y", Type = "double", Required = false, Description = "Placement Y in mm on sheet" }
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
