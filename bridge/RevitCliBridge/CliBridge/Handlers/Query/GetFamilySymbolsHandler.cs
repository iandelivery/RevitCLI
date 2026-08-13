using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Query
{
    public class GetFamilySymbolsHandler : DocumentCommandBase
    {
        public override string CommandName => "get_family_symbols";
        public override string Description => "Retrieves family symbols, optionally filtered by family name and/or category";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "family_name", Type = "string", Required = false, Description = "Filter by family name (contains match)" },
            new CommandParamSchema { Name = "category", Type = "string", Required = false, Description = "BuiltInCategory enum value to filter" },
            new CommandParamSchema { Name = "limit", Type = "int", Required = false, Description = "Maximum number of results (page size)", Default = 500 },
            new CommandParamSchema { Name = "offset", Type = "int", Required = false, Description = "Number of results to skip for pagination", Default = 0 }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_family_symbols\", \"parameters\": {} }",
            "{ \"command\": \"get_family_symbols\", \"parameters\": { \"family_name\": \"M_Single-Flush\" } }",
            "{ \"command\": \"get_family_symbols\", \"parameters\": { \"category\": \"OST_Doors\" } }",
            "{ \"command\": \"get_family_symbols\", \"parameters\": { \"limit\": 100, \"offset\": 100 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            string? familyName = HandlerUtilities.GetStringOrNull(parameters, "family_name");
            string? categoryStr = HandlerUtilities.GetStringOrNull(parameters, "category");
            var (limit, offset) = HandlerUtilities.GetPagingParams(parameters);

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

            // OrderBy(id) ensures stable pagination; Take(limit+1) enables
            // has_more detection without a full Count() over the source.
            var overFetched = symbols
                .Select(s => new
                {
                    element_id = s.Id.IntegerValue,
                    family_name = s.Family.Name,
                    symbol_name = s.Name,
                    category = s.Category?.Name
                })
                .OrderBy(e => e.element_id)
                .Skip(offset)
                .Take(limit + 1)
                .ToList();

            var (items, hasMore) = HandlerUtilities.ApplyPaging(overFetched, limit);

            var result = new
            {
                count = items.Count,
                offset = offset,
                limit = limit,
                has_more = hasMore,
                symbols = items
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Retrieved {items.Count} family symbols.").ToJson();
        }
    }
}
