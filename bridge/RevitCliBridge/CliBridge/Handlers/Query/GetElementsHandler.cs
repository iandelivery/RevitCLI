using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers.Query
{
    public class GetElementsHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_elements";
        public override string Description => "Retrieves elements from the active document or a linked document, optionally filtered by category";
        public override string Category => "Query";

        protected override string ItemsProperty => "elements";
        protected override string SuccessMessage(int count) => $"Retrieved {count} elements.";

        protected override CommandParamSchema[] BaseParameters => new[]
        {
            new CommandParamSchema { Name = "category", Type = "string", Required = false, Description = "BuiltInCategory enum value to filter (e.g. 'OST_Walls', 'OST_Doors')" },
            new CommandParamSchema { Name = "link", Type = "string", Required = false, Description = "Linked document to query: RevitLinkInstance id or exact link name (see get_links). Default: the host document." }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_elements\", \"parameters\": {} }",
            "{ \"command\": \"get_elements\", \"parameters\": { \"category\": \"OST_Walls\" } }",
            "{ \"command\": \"get_elements\", \"parameters\": { \"category\": \"OST_Walls\", \"limit\": 100, \"offset\": 200 } }",
            "{ \"command\": \"get_elements\", \"parameters\": { \"category\": \"OST_Walls\", \"link\": 184324 } }"
        };

        protected override string? Validate(Dictionary<string, object> parameters)
        {
            return ValidateLinkOrNull(parameters);
        }

        internal static string? ValidateLinkOrNull(Dictionary<string, object> parameters)
        {
            if (!parameters.ContainsKey("link"))
                return null;

            var selector = HandlerUtilities.ResolveLinkSelector(
                HandlerUtilities.GetStringOrNull(parameters, "link"));
            if (selector is null)
                return "Parameter 'link' is empty.";

            return null;
        }

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            string? category = null;
            if (parameters.TryGetValue("category", out var catVal))
                category = catVal?.ToString();

            var target = HandlerUtilities.TryResolveTargetDocument(doc, parameters, out var linkLabel);
            if (target is null)
                throw new InvalidOperationException($"Linked document not loaded or not found: {linkLabel}");

            int? linkId = null;
            string? linkName = null;
            if (linkLabel is not null)
            {
                var selector = HandlerUtilities.ResolveLinkSelector(
                    HandlerUtilities.GetStringOrNull(parameters, "link"));
                var instance = FindInstance(doc, selector);
                linkId = instance?.Id.IntegerValue;
                linkName = instance?.Name;
            }

            var collector = new FilteredElementCollector(target);

            if (!string.IsNullOrEmpty(category))
            {
                try
                {
                    var bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), category);
                    collector = collector.OfCategory(bic);
                }
                catch
                {
                    collector = collector.WhereElementIsNotElementType();
                }
            }
            else
            {
                collector = collector.WhereElementIsNotElementType();
            }

            return collector
                .WhereElementIsNotElementType()
                .Select(e => new
                {
                    id = e.Id.IntegerValue,
                    name = e.Name,
                    category = e.Category?.Name,
                    class_type = e.GetType().Name,
                    link_id = linkId,
                    link_name = linkName
                })
                .OrderBy(e => e.id)
                .Select(e => (object)e);
        }

        internal static RevitLinkInstance? FindInstance(Document hostDoc, LinkSelector? selector)
        {
            if (selector is null)
                return null;

            var instances = new FilteredElementCollector(hostDoc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            if (selector.InstanceId.HasValue)
                return instances.FirstOrDefault(l => l.Id.IntegerValue == selector.InstanceId.Value);

            return instances.FirstOrDefault(l =>
                string.Equals(l.Name, selector.Name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
