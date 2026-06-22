using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class GetFamilySymbolHandler : DocumentCommandBase
    {
        public override string CommandName => "get_family_symbol";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? instanceId = HandlerUtilities.GetIntOrNull(parameters, "instance_id");
            if (instanceId is not null)
                return HandleFromInstance(doc, cmd, instanceId.Value);

            string? familyName = HandlerUtilities.GetStringOrNull(parameters, "family_name");
            string? symbolName = HandlerUtilities.GetStringOrNull(parameters, "symbol_name");

            if (!string.IsNullOrEmpty(familyName) && !string.IsNullOrEmpty(symbolName))
                return HandleByName(doc, cmd, familyName, symbolName, parameters);

            return CommandResponse.Error(cmd.TaskId,
                "Missing required parameters. Provide either instance_id or both family_name and symbol_name.").ToJson();
        }

        private string HandleFromInstance(Document doc, QueuedCommand cmd, int instanceId)
        {
            var instance = doc.GetElement(new ElementId(instanceId)) as FamilyInstance;
            if (instance is null)
                return CommandResponse.Error(cmd.TaskId, $"FamilyInstance with ID {instanceId} not found.").ToJson();

            var symbol = instance.Symbol;
            if (symbol is null)
                return CommandResponse.Error(cmd.TaskId, "Could not retrieve symbol from the family instance.").ToJson();

            var result = new
            {
                instance_id = instanceId,
                symbol_id = symbol.Id.IntegerValue,
                symbol_name = symbol.Name,
                family_name = symbol.Family?.Name
            };

            return CommandResponse.Success(cmd.TaskId, result, "Family symbol retrieved successfully.").ToJson();
        }

        private string HandleByName(Document doc, QueuedCommand cmd, string familyName, string symbolName, Dictionary<string, object> parameters)
        {
            string? categoryStr = HandlerUtilities.GetStringOrNull(parameters, "category");

            BuiltInCategory bic = BuiltInCategory.INVALID;
            if (!string.IsNullOrEmpty(categoryStr))
            {
                if (!Enum.TryParse(categoryStr, out bic))
                    return CommandResponse.Error(cmd.TaskId,
                        $"Invalid BuiltInCategory: {categoryStr}.").ToJson();
            }

            var collector = new FilteredElementCollector(doc);

            if (bic != BuiltInCategory.INVALID)
            {
                collector.OfCategory(bic);
            }

            var symbols = collector
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>();

            var matches = new List<FamilySymbol>();
            foreach (var sym in symbols)
            {
                if (sym.Family.Name == familyName && sym.Name == symbolName)
                {
                    matches.Add(sym);
                }
            }

            if (matches.Count == 0)
                return CommandResponse.Error(cmd.TaskId,
                    $"No family symbol found with family '{familyName}' and symbol '{symbolName}'.").ToJson();

            var results = new List<object>();
            foreach (var sym in matches)
            {
                results.Add(new
                {
                    element_id = sym.Id.IntegerValue,
                    family_name = sym.Family.Name,
                    symbol_name = sym.Name,
                    category = sym.Category?.Name
                });
            }

            return CommandResponse.Success(cmd.TaskId, results,
                $"Found {results.Count} family symbol(s).").ToJson();
        }
    }
}
