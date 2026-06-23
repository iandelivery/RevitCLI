using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class CreateSheetHandler : DocumentCommandBase
    {
        public override string CommandName => "create_sheet";
        public override string Description => "Creates a new sheet with an optional titleblock";
        public override string Category => "Create";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "name", Type = "string", Required = true, Description = "Sheet name" },
            new CommandParamSchema { Name = "number", Type = "string", Required = true, Description = "Sheet number" },
            new CommandParamSchema { Name = "titleblock_id", Type = "int", Required = false, Description = "Titleblock element type ID (uses first available if omitted)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_sheet\", \"parameters\": { \"name\": \"Floor Plan\", \"number\": \"A-101\" } }",
            "{ \"command\": \"create_sheet\", \"parameters\": { \"name\": \"Floor Plan\", \"number\": \"A-101\", \"titleblock_id\": 12345 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            string? sheetName = HandlerUtilities.GetStringOrNull(parameters, "name");
            string? sheetNumber = HandlerUtilities.GetStringOrNull(parameters, "number");
            int? titleblockId = HandlerUtilities.GetIntOrNull(parameters, "titleblock_id");

            if (string.IsNullOrEmpty(sheetName))
                return CommandResponse.Error(cmd.TaskId, "Missing 'name' parameter.").ToJson();

            if (string.IsNullOrEmpty(sheetNumber))
                return CommandResponse.Error(cmd.TaskId, "Missing 'number' parameter.").ToJson();

            ElementId tbId = ElementId.InvalidElementId;
            if (titleblockId.HasValue && titleblockId.Value > 0)
            {
                var tbElement = doc.GetElement(new ElementId(titleblockId.Value));
                if (tbElement is not null)
                    tbId = tbElement.Id;
            }
            else
            {
                var titleBlock = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsElementType()
                    .FirstOrDefault();

                if (titleBlock is not null)
                    tbId = titleBlock.Id;
            }

            ViewSheet? sheet = null;

            using (Transaction t = new Transaction(doc, "CLI Create Sheet"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                try
                {
                    sheet = ViewSheet.Create(doc, tbId);
                    if (sheet is null)
                        return CommandResponse.Error(cmd.TaskId, "Failed to create sheet.").ToJson();

                    sheet.Name = sheetName;
                    sheet.SheetNumber = sheetNumber;
                }
                catch (Exception ex)
                {
                    return CommandResponse.Error(cmd.TaskId, $"Failed to create sheet: {ex.Message}").ToJson();
                }

                t.Commit();
            }

            var result = new
            {
                element_id = sheet.Id.IntegerValue,
                name = sheet.Name,
                sheet_number = sheet.SheetNumber
            };

            return CommandResponse.Success(cmd.TaskId, result, "Sheet created successfully.").ToJson();
        }
    }
}
