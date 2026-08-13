using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Query
{
    public class GetElementsHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_elements";
        public override string Description => "Retrieves elements from the active document, optionally filtered by category";
        public override string Category => "Query";

        protected override string ItemsProperty => "elements";
        protected override string SuccessMessage(int count) => $"Retrieved {count} elements.";

        protected override CommandParamSchema[] BaseParameters => new[]
        {
            new CommandParamSchema { Name = "category", Type = "string", Required = false, Description = "BuiltInCategory enum value to filter (e.g. 'OST_Walls', 'OST_Doors')" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_elements\", \"parameters\": {} }",
            "{ \"command\": \"get_elements\", \"parameters\": { \"category\": \"OST_Walls\" } }",
            "{ \"command\": \"get_elements\", \"parameters\": { \"category\": \"OST_Walls\", \"limit\": 100, \"offset\": 200 } }"
        };

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
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

            // OrderBy(id) ensures stable pagination.
            return collector
                .WhereElementIsNotElementType()
                .Select(e => new
                {
                    id = e.Id.IntegerValue,
                    name = e.Name,
                    category = e.Category?.Name,
                    class_type = e.GetType().Name
                })
                .OrderBy(e => e.id)
                .Select(e => (object)e);
        }
    }
}
