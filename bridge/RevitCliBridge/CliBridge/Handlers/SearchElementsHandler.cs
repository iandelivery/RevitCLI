using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class SearchElementsHandler : DocumentCommandBase
    {
        public override string CommandName => "search_elements";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            string? category = HandlerUtilities.GetStringOrNull(parameters, "category");
            string? paramName = HandlerUtilities.GetStringOrNull(parameters, "param_name");
            string? paramValue = HandlerUtilities.GetStringOrNull(parameters, "param_value");
            string? paramOperator = HandlerUtilities.GetStringOrNull(parameters, "param_operator") ?? "eq";
            int? limit = HandlerUtilities.GetIntOrNull(parameters, "limit") ?? 500;

            if (string.IsNullOrEmpty(category))
                return CommandResponse.Error(cmd.TaskId, "Missing 'category' parameter.").ToJson();

            if (string.IsNullOrEmpty(paramName))
                return CommandResponse.Error(cmd.TaskId, "Missing 'param_name' parameter.").ToJson();

            if (paramValue is null && paramOperator != "empty")
                return CommandResponse.Error(cmd.TaskId, "Missing 'param_value' parameter.").ToJson();

            BuiltInCategory bic;
            try
            {
                bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), category);
            }
            catch
            {
                return CommandResponse.Error(cmd.TaskId, $"Invalid category: {category}").ToJson();
            }

            var collector = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType();

            var elements = collector
                .Where(e => MatchesParameter(e, paramName!, paramValue, paramOperator))
                .Select(e => new
                {
                    id = e.Id.IntegerValue,
                    name = e.Name,
                    category = e.Category?.Name,
                    class_type = e.GetType().Name
                })
                .Take(limit.Value)
                .ToList();

            var result = new
            {
                category = category,
                param_name = paramName,
                param_operator = paramOperator,
                param_value = paramValue,
                count = elements.Count,
                elements = elements
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Found {elements.Count} elements matching criteria.").ToJson();
        }

        private bool MatchesParameter(Element element, string paramName, string? paramValue, string op)
        {
            var parameter = element.LookupParameter(paramName);
            if (parameter is null)
                return op == "empty";

            string? currentValue = parameter.AsValueString();

            switch (op.ToLower())
            {
                case "eq":
                    return string.Equals(currentValue, paramValue, StringComparison.OrdinalIgnoreCase);

                case "neq":
                    return !string.Equals(currentValue, paramValue, StringComparison.OrdinalIgnoreCase);

                case "contains":
                    return currentValue is not null && currentValue.IndexOf(paramValue ?? "", StringComparison.OrdinalIgnoreCase) >= 0;

                case "gt":
                    return TryCompareNumeric(currentValue, paramValue, out int cmpGt) && cmpGt > 0;

                case "lt":
                    return TryCompareNumeric(currentValue, paramValue, out int cmpLt) && cmpLt < 0;

                case "empty":
                    return string.IsNullOrEmpty(currentValue);

                case "notempty":
                    return !string.IsNullOrEmpty(currentValue);

                default:
                    return string.Equals(currentValue, paramValue, StringComparison.OrdinalIgnoreCase);
            }
        }

        private bool TryCompareNumeric(string? left, string? right, out int comparison)
        {
            comparison = 0;
            if (left is null || right is null) return false;

            if (double.TryParse(left, out double lVal) && double.TryParse(right, out double rVal))
            {
                comparison = lVal.CompareTo(rVal);
                return true;
            }

            comparison = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            return true;
        }
    }
}
