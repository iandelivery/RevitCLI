using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class GetSheetsHandler : DocumentCommandBase
    {
        public override string CommandName => "get_sheets";
        public override string Description => "Retrieves all sheets in the active document";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => Array.Empty<CommandParamSchema>();

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_sheets\", \"parameters\": {} }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet));

            var sheets = collector
                .Cast<ViewSheet>()
                .Select(s => new
                {
                    id = s.Id.IntegerValue,
                    name = s.Name,
                    sheet_number = s.SheetNumber,
                    is_placeholder = s.IsPlaceholder,
                    viewport_count = s.GetAllViewports().Count
                })
                .ToList();

            var result = new
            {
                count = sheets.Count,
                sheets = sheets
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Retrieved {sheets.Count} sheets.").ToJson();
        }
    }
}
