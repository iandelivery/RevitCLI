using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class GetElementTypesHandler : DocumentCommandBase
    {
        public override string CommandName => "get_element_types";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
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

            var results = types
                .Select(e => new
                {
                    element_id = e.Id.IntegerValue,
                    name = e.Name,
                    family_name = e.FamilyName,
                    category = e.Category?.Name,
                    class_type = e.GetType().Name
                })
                .Take(500)
                .ToList();

            var result = new { count = results.Count, types = results };
            return CommandResponse.Success(cmd.TaskId, result,
                $"Retrieved {results.Count} element types.").ToJson();
        }
    }
}
