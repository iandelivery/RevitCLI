using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class GetElementsHandler : DocumentCommandBase
    {
        public override string CommandName => "get_elements";
        public override string Description => "Retrieves elements from the active document, optionally filtered by category";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "category", Type = "string", Required = false, Description = "BuiltInCategory enum value to filter (e.g. 'OST_Walls', 'OST_Doors')" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_elements\", \"parameters\": {} }",
            "{ \"command\": \"get_elements\", \"parameters\": { \"category\": \"OST_Walls\" } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            string? category = null;
            if (parameters.TryGetValue("category", out var catVal))
                category = catVal?.ToString();

            var collector = new FilteredElementCollector(doc);

            if (!string.IsNullOrEmpty(category))
            {
                try
                {
                    var bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), category);
                    collector = collector.OfCategory(bic);
                }
                catch
                {
                    collector = collector.WhereElementIsNotElementType();
                }
            }
            else
            {
                collector = collector.WhereElementIsNotElementType();
            }

            var elements = collector
                .WhereElementIsNotElementType()
                .Select(e => new
                {
                    id = e.Id.IntegerValue,
                    name = e.Name,
                    category = e.Category?.Name,
                    class_type = e.GetType().Name
                })
                .Take(500)
                .ToList();

            var result = new
            {
                count = elements.Count,
                elements = elements
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Retrieved {elements.Count} elements.").ToJson();
        }
    }
}