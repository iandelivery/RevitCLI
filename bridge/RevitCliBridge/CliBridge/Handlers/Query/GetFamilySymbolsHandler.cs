using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Query
{
    public class GetFamilySymbolsHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_family_symbols";
        public override string Description => "Retrieves family symbols, optionally filtered by family name and/or category";
        public override string Category => "Query";

        protected override string ItemsProperty => "symbols";
        protected override string SuccessMessage(int count) => $"Retrieved {count} family symbols.";

        protected override CommandParamSchema[] BaseParameters => new[]
        {
            new CommandParamSchema { Name = "family_name", Type = "string", Required = false, Description = "Filter by family name (contains match)" },
            new CommandParamSchema { Name = "category", Type = "string", Required = false, Description = "BuiltInCategory enum value to filter" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_family_symbols\", \"parameters\": {} }",
            "{ \"command\": \"get_family_symbols\", \"parameters\": { \"family_name\": \"M_Single-Flush\" } }",
            "{ \"command\": \"get_family_symbols\", \"parameters\": { \"category\": \"OST_Doors\" } }",
            "{ \"command\": \"get_family_symbols\", \"parameters\": { \"limit\": 100, \"offset\": 100 } }"
        };

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            string? familyName = HandlerUtilities.GetStringOrNull(parameters, "family_name");
            string? categoryStr = HandlerUtilities.GetStringOrNull(parameters, "category");

            var collector = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));

            if (!string.IsNullOrEmpty(categoryStr))
            {
                if (Enum.TryParse(categoryStr, out BuiltInCategory bic))
                {
                    collector.OfCategory(bic);
                }
            }

            var symbols = collector.Cast<FamilySymbol>();

            if (!string.IsNullOrEmpty(familyName))
            {
                symbols = symbols.Where(s => s.Family.Name.Contains(familyName));
            }

            // OrderBy(id) ensures stable pagination.
            return symbols
                .Select(s => new
                {
                    element_id = s.Id.IntegerValue,
                    family_name = s.Family.Name,
                    symbol_name = s.Name,
                    category = s.Category?.Name
                })
                .OrderBy(e => e.element_id)
                .Select(e => (object)e);
        }
    }
}
