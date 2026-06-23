using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class ApplyViewTemplateHandler : DocumentCommandBase
    {
        public override string CommandName => "apply_view_template";
        public override string Description => "Applies a view template to one or more views";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "template_id", Type = "int", Required = true, Description = "View template element ID" },
            new CommandParamSchema { Name = "view_ids", Type = "int[]", Required = false, Description = "View element IDs to apply template to" },
            new CommandParamSchema { Name = "all_non_template", Type = "bool", Required = false, Description = "Apply to all non-template views" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"apply_view_template\", \"parameters\": { \"template_id\": 12345, \"view_ids\": [678, 789] } }",
            "{ \"command\": \"apply_view_template\", \"parameters\": { \"template_id\": 12345, \"all_non_template\": true } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? templateId = HandlerUtilities.GetIntOrNull(parameters, "template_id");
            string? templateName = HandlerUtilities.GetStringOrNull(parameters, "template_name");

            if (!templateId.HasValue && string.IsNullOrEmpty(templateName))
                return CommandResponse.Error(cmd.TaskId, "Missing 'template_id' or 'template_name' parameter.").ToJson();

            ElementId? resolvedTemplateId = null;

            if (templateId.HasValue)
            {
                var tplElement = doc.GetElement(new ElementId(templateId.Value));
                if (tplElement is View tplView && tplView.IsTemplate)
                    resolvedTemplateId = tplView.Id;
                else
                    return CommandResponse.Error(cmd.TaskId, $"Element with ID {templateId.Value} is not a view template.").ToJson();
            }
            else
            {
                var template = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => v.IsTemplate && v.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));

                if (template is null)
                    return CommandResponse.Error(cmd.TaskId, $"View template '{templateName}' not found.").ToJson();

                resolvedTemplateId = template.Id;
            }

            var targetViews = ResolveTargetViews(doc, parameters);
            if (targetViews is null || targetViews.Count == 0)
                return CommandResponse.Error(cmd.TaskId, "No target views found. Use view_id, view_ids, view_type, or all_views.").ToJson();

            int successCount = 0;
            int failCount = 0;
            var results = new List<object>();

            using (Transaction t = new Transaction(doc, "CLI Apply View Template"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                foreach (var view in targetViews)
                {
                    try
                    {
                        if (view.IsTemplate)
                        {
                            results.Add(new { view_id = view.Id.IntegerValue, view_name = view.Name, success = false, error = "Cannot apply template to a view template" });
                            failCount++;
                            continue;
                        }

                        view.ViewTemplateId = resolvedTemplateId;
                        results.Add(new { view_id = view.Id.IntegerValue, view_name = view.Name, success = true, error = (string?)null });
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { view_id = view.Id.IntegerValue, view_name = view.Name, success = false, error = ex.Message });
                        failCount++;
                    }
                }

                t.Commit();
            }

            var result = new
            {
                template_id = resolvedTemplateId.IntegerValue,
                total = targetViews.Count,
                success_count = successCount,
                fail_count = failCount,
                results = results
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Applied view template: {successCount} succeeded, {failCount} failed.").ToJson();
        }

        private List<View>? ResolveTargetViews(Document doc, Dictionary<string, object> parameters)
        {
            int? viewId = HandlerUtilities.GetIntOrNull(parameters, "view_id");
            if (viewId.HasValue)
            {
                var view = doc.GetElement(new ElementId(viewId.Value)) as View;
                if (view is not null && !view.IsTemplate)
                    return new List<View> { view };
                return null;
            }

            var viewIds = HandlerUtilities.GetIntArrayOrNull(parameters, "view_ids");
            if (viewIds is not null && viewIds.Length > 0)
            {
                var views = new List<View>();
                foreach (var id in viewIds)
                {
                    var view = doc.GetElement(new ElementId(id)) as View;
                    if (view is not null && !view.IsTemplate)
                        views.Add(view);
                }
                return views;
            }

            string? viewType = HandlerUtilities.GetStringOrNull(parameters, "view_type");
            if (!string.IsNullOrEmpty(viewType))
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && string.Equals(v.ViewType.ToString(), viewType, StringComparison.OrdinalIgnoreCase))
                    .Take(500)
                    .ToList();
            }

            bool allViews = false;
            if (parameters.TryGetValue("all_views", out var allVal) && allVal is not null)
                allViews = Convert.ToBoolean(allVal);

            if (allViews)
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .Take(500)
                    .ToList();
            }

            return null;
        }
    }
}
