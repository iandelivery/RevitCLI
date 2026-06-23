using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class GetSymbolInstancesHandler : DocumentCommandBase
    {
        public override string CommandName => "get_symbol_instances";
        public override string Description => "Retrieves all instances of a specific family symbol";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "symbol_id", Type = "int", Required = true, Description = "FamilySymbol element ID" },
            new CommandParamSchema { Name = "view_id", Type = "int", Required = false, Description = "View element ID to scope the search" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_symbol_instances\", \"parameters\": { \"symbol_id\": 12345 } }",
            "{ \"command\": \"get_symbol_instances\", \"parameters\": { \"symbol_id\": 12345, \"view_id\": 67890 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? symbolId = HandlerUtilities.GetIntOrNull(parameters, "symbol_id");
            if (symbolId is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: symbol_id.").ToJson();

            var symbolElem = doc.GetElement(new ElementId(symbolId.Value));
            if (symbolElem is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {symbolId.Value} not found.").ToJson();

            var symbol = symbolElem as FamilySymbol;
            if (symbol is null)
                return CommandResponse.Error(cmd.TaskId, $"Element {symbolId.Value} is not a FamilySymbol.").ToJson();

            int? viewId = HandlerUtilities.GetIntOrNull(parameters, "view_id");

            FilteredElementCollector collector;
            if (viewId is not null)
            {
                var viewElem = doc.GetElement(new ElementId(viewId.Value));
                if (viewElem is null || !(viewElem is View))
                    return CommandResponse.Error(cmd.TaskId, $"View with ID {viewId.Value} not found.").ToJson();

                collector = new FilteredElementCollector(doc, new ElementId(viewId.Value));
            }
            else
            {
                collector = new FilteredElementCollector(doc);
            }

            var matched = collector
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>();

            var instances = new List<object>();
            foreach (var inst in matched)
            {
                if (inst.Symbol.Id.IntegerValue == symbolId.Value)
                {
                    instances.Add(new
                    {
                        element_id = inst.Id.IntegerValue,
                        name = inst.Name,
                        category = inst.Category?.Name,
                        level_id = inst.LevelId?.IntegerValue ?? -1
                    });
                }
            }

            var result = new
            {
                symbol_id = symbolId.Value,
                symbol_name = symbol.Name,
                family_name = symbol.Family?.Name,
                count = instances.Count,
                instances = instances
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Found {instances.Count} instance(s) of symbol '{symbol.Name}'.").ToJson();
        }
    }
}
