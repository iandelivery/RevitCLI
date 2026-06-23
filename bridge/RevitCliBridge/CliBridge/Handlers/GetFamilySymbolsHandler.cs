using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class GetFamilySymbolsHandler : DocumentCommandBase
    {
        public override string CommandName => "get_family_symbols";
        public override string Description => "Retrieves family symbols, optionally filtered by family name and/or category";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "family_name", Type = "string", Required = false, Description = "Filter by family name (contains match)" },
            new CommandParamSchema { Name = "category", Type = "string", Required = false, Description = "BuiltInCategory enum value to filter" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_family_symbols\", \"parameters\": {} }",
            "{ \"command\": \"get_family_symbols\", \"parameters\": { \"family_name\": \"M_Single-Flush\" } }",
            "{ \"command\": \"get_family_symbols\", \"parameters\": { \"category\": \"OST_Doors\" } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
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

            var results = symbols
                .Select(s => new
                {
                    element_id = s.Id.IntegerValue,
                    family_name = s.Family.Name,
                    symbol_name = s.Name,
                    category = s.Category?.Name
                })
                .Take(500)
                .ToList();

            var result = new { count = results.Count, symbols = results };
            return CommandResponse.Success(cmd.TaskId, result,
                $"Retrieved {results.Count} family symbols.").ToJson();
        }
    }
}
