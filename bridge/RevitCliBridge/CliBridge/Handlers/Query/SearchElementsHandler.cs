using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Query
{
    public class SearchElementsHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "search_elements";
        public override string Description => "Searches elements by category and parameter value with various comparison operators";
        public override string Category => "Query";

        protected override string ItemsProperty => "elements";
        protected override string SuccessMessage(int count) => $"Found {count} elements matching criteria.";

        protected override CommandParamSchema[] BaseParameters => new[]
        {
            new CommandParamSchema { Name = "category", Type = "string", Required = true, Description = "BuiltInCategory enum value (e.g. 'OST_Walls')" },
            new CommandParamSchema { Name = "param_name", Type = "string", Required = true, Description = "Parameter name to search on" },
            new CommandParamSchema { Name = "param_value", Type = "string", Required = false, Description = "Parameter value to compare (not required for 'empty' operator)" },
            new CommandParamSchema { Name = "param_operator", Type = "string", Required = false, Description = "Comparison operator", EnumValues = new[] { "eq", "neq", "contains", "gt", "lt", "empty", "notempty" }, Default = "eq" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"search_elements\", \"parameters\": { \"category\": \"OST_Walls\", \"param_name\": \"Comments\", \"param_value\": \"Review\", \"param_operator\": \"contains\" } }",
            "{ \"command\": \"search_elements\", \"parameters\": { \"category\": \"OST_Doors\", \"param_name\": \"Mark\", \"param_value\": \"A-1\", \"param_operator\": \"eq\" } }",
            "{ \"command\": \"search_elements\", \"parameters\": { \"category\": \"OST_Walls\", \"param_name\": \"Comments\", \"param_operator\": \"empty\" } }",
            "{ \"command\": \"search_elements\", \"parameters\": { \"category\": \"OST_Walls\", \"param_name\": \"Comments\", \"param_operator\": \"contains\", \"param_value\": \"Review\", \"limit\": 100, \"offset\": 100 } }"
        };

        protected override string? Validate(Dictionary<string, object> parameters)
        {
            string? category = HandlerUtilities.GetStringOrNull(parameters, "category");
            string? paramName = HandlerUtilities.GetStringOrNull(parameters, "param_name");
            string? paramValue = HandlerUtilities.GetStringOrNull(parameters, "param_value");
            string? paramOperator = HandlerUtilities.GetStringOrNull(parameters, "param_operator") ?? "eq";

            if (string.IsNullOrEmpty(category))
                return "Missing 'category' parameter.";

            if (string.IsNullOrEmpty(paramName))
                return "Missing 'param_name' parameter.";

            if (paramValue is null && paramOperator != "empty")
                return "Missing 'param_value' parameter.";

            if (!Enum.TryParse(category, out BuiltInCategory _))
                return $"Invalid category: {category}";

            return null;
        }

        protected override void MergeExtraFields(Dictionary<string, object> result, Dictionary<string, object> parameters)
        {
            result["category"] = HandlerUtilities.GetStringOrNull(parameters, "category") ?? "";
            result["param_name"] = HandlerUtilities.GetStringOrNull(parameters, "param_name") ?? "";
            result["param_operator"] = HandlerUtilities.GetStringOrNull(parameters, "param_operator") ?? "eq";
            result["param_value"] = (object?)HandlerUtilities.GetStringOrNull(parameters, "param_value") ?? "";
        }

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            string? category = HandlerUtilities.GetStringOrNull(parameters, "category");
            string? paramName = HandlerUtilities.GetStringOrNull(parameters, "param_name");
            string? paramValue = HandlerUtilities.GetStringOrNull(parameters, "param_value");
            string? paramOperator = HandlerUtilities.GetStringOrNull(parameters, "param_operator") ?? "eq";

            // Validate already checked these are non-null / valid enum.
            var bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), category);

            var collector = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType();

            // OrderBy(id) ensures stable pagination.
            return collector
                .Where(e => MatchesParameter(e, paramName!, paramValue, paramOperator))
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
