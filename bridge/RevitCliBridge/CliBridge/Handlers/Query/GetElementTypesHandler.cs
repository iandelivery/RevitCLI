using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Query
{
    public class GetElementTypesHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_element_types";
        public override string Description => "Retrieves element types (excluding FamilySymbol), optionally filtered by category or name";
        public override string Category => "Query";

        protected override string ItemsProperty => "types";
        protected override string SuccessMessage(int count) => $"Retrieved {count} element types.";

        protected override CommandParamSchema[] BaseParameters => new[]
        {
            new CommandParamSchema { Name = "type_name", Type = "string", Required = false, Description = "Filter by type name (contains match)" },
            new CommandParamSchema { Name = "category", Type = "string", Required = false, Description = "BuiltInCategory enum value to filter" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_element_types\", \"parameters\": {} }",
            "{ \"command\": \"get_element_types\", \"parameters\": { \"category\": \"OST_Walls\" } }",
            "{ \"command\": \"get_element_types\", \"parameters\": { \"type_name\": \"Concrete\" } }",
            "{ \"command\": \"get_element_types\", \"parameters\": { \"limit\": 100, \"offset\": 100 } }"
        };

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            string? typeName = HandlerUtilities.GetStringOrNull(parameters, "type_name");
            string? categoryStr = HandlerUtilities.GetStringOrNull(parameters, "category");

            var collector = new FilteredElementCollector(doc).OfClass(typeof(ElementType));

            if (!string.IsNullOrEmpty(categoryStr))
            {
                if (Enum.TryParse(categoryStr, out BuiltInCategory bic))
                {
                    collector.OfCategory(bic);
                }
            }

            var types = collector
                .Cast<ElementType>()
                .Where(e => !(e is FamilySymbol));

            if (!string.IsNullOrEmpty(typeName))
            {
                types = types.Where(e => e.Name.Contains(typeName));
            }

            // OrderBy(id) ensures stable pagination.
            return types
                .Select(e => new
                {
                    element_id = e.Id.IntegerValue,
                    name = e.Name,
                    family_name = e.FamilyName,
                    category = e.Category?.Name,
                    class_type = e.GetType().Name
                })
                .OrderBy(e => e.element_id)
                .Select(e => (object)e);
        }
    }
}
