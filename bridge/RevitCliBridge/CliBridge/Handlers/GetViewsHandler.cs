using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class GetViewsHandler : DocumentCommandBase
    {
        public override string CommandName => "get_views";
        public override string Description => "Retrieves views, optionally filtered by type, template, or template status";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "type", Type = "string", Required = false, Description = "View type filter (e.g. 'FloorPlan', 'Section', 'ThreeD')" },
            new CommandParamSchema { Name = "template", Type = "string", Required = false, Description = "View template name filter" },
            new CommandParamSchema { Name = "is_template", Type = "bool", Required = false, Description = "If true, include view templates in results" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_views\", \"parameters\": {} }",
            "{ \"command\": \"get_views\", \"parameters\": { \"type\": \"FloorPlan\" } }",
            "{ \"command\": \"get_views\", \"parameters\": { \"is_template\": true } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            string? viewType = HandlerUtilities.GetStringOrNull(parameters, "type");
            string? templateName = HandlerUtilities.GetStringOrNull(parameters, "template");
            bool? isTemplate = null;
            if (parameters.TryGetValue("is_template", out var isTplVal) && isTplVal is not null)
                isTemplate = Convert.ToBoolean(isTplVal);

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(View));

            var views = collector
                .Cast<View>()
                .Where(v => !v.IsTemplate || isTemplate == true)
                .Where(v => MatchesViewType(v, viewType))
                .Where(v => MatchesTemplate(v, doc, templateName))
                .Where(v => isTemplate is null || v.IsTemplate == isTemplate.Value)
                .Select(v => new
                {
                    id = v.Id.IntegerValue,
                    name = v.Name,
                    view_type = v.ViewType.ToString(),
                    is_template = v.IsTemplate,
                    template_id = v.ViewTemplateId.IntegerValue == -1
                        ? (int?)null
                        : v.ViewTemplateId.IntegerValue
                })
                .ToList();

            var result = new
            {
                count = views.Count,
                views = views
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Retrieved {views.Count} views.").ToJson();
        }

        private bool MatchesViewType(View view, string? viewType)
        {
            if (string.IsNullOrEmpty(viewType))
                return true;

            return string.Equals(view.ViewType.ToString(), viewType, StringComparison.OrdinalIgnoreCase);
        }

        private bool MatchesTemplate(View view, Document doc, string? templateName)
        {
            if (string.IsNullOrEmpty(templateName))
                return true;

            if (view.ViewTemplateId == ElementId.InvalidElementId)
                return false;

            var template = doc.GetElement(view.ViewTemplateId) as View;
            return template is not null && template.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
