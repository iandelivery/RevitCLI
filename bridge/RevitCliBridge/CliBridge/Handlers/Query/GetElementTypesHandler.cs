using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Query
{
    public class GetElementTypesHandler : DocumentCommandBase
    {
        public override string CommandName => "get_element_types";
        public override string Description => "Retrieves element types (excluding FamilySymbol), optionally filtered by category or name";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "type_name", Type = "string", Required = false, Description = "Filter by type name (contains match)" },
            new CommandParamSchema { Name = "category", Type = "string", Required = false, Description = "BuiltInCategory enum value to filter" },
            new CommandParamSchema { Name = "limit", Type = "int", Required = false, Description = "Maximum number of results (page size)", Default = 500 },
            new CommandParamSchema { Name = "offset", Type = "int", Required = false, Description = "Number of results to skip for pagination", Default = 0 }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_element_types\", \"parameters\": {} }",
            "{ \"command\": \"get_element_types\", \"parameters\": { \"category\": \"OST_Walls\" } }",
            "{ \"command\": \"get_element_types\", \"parameters\": { \"type_name\": \"Concrete\" } }",
            "{ \"command\": \"get_element_types\", \"parameters\": { \"limit\": 100, \"offset\": 100 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            string? typeName = HandlerUtilities.GetStringOrNull(parameters, "type_name");
            string? categoryStr = HandlerUtilities.GetStringOrNull(parameters, "category");
            var (limit, offset) = HandlerUtilities.GetPagingParams(parameters);

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

            // OrderBy(id) ensures stable pagination; Take(limit+1) enables
            // has_more detection without a full Count() over the source.
            var overFetched = types
                .Select(e => new
                {
                    element_id = e.Id.IntegerValue,
                    name = e.Name,
                    family_name = e.FamilyName,
                    category = e.Category?.Name,
                    class_type = e.GetType().Name
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
                types = items
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Retrieved {items.Count} element types.").ToJson();
        }
    }
}
