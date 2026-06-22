using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class BatchSetParamHandler : DocumentCommandBase
    {
        public override string CommandName => "batch_set_param";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var uiDoc = app.ActiveUIDocument;

            if (!parameters.TryGetValue("parameter_name", out var nameVal) || nameVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'parameter_name' parameter.").ToJson();

            if (!parameters.TryGetValue("value", out var valVal))
                return CommandResponse.Error(cmd.TaskId, "Missing 'value' parameter.").ToJson();

            string paramName = nameVal.ToString() ?? "";
            object value = valVal;

            var elementIds = ResolveElementIds(doc, uiDoc!, parameters);
            if (elementIds is null)
                return CommandResponse.Error(cmd.TaskId, "No target elements specified. Use element_ids, category, or use_selection.").ToJson();

            if (elementIds.Count == 0)
                return CommandResponse.Error(cmd.TaskId, "No elements found matching the specified criteria.").ToJson();

            var results = new List<object>();
            int successCount = 0;
            int failCount = 0;
            int totalElements = elementIds.Count;

            using (Transaction t = new Transaction(doc, "CLI Batch Set Parameter"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                for (int i = 0; i < elementIds.Count; i++)
                {
                    var eid = elementIds[i];

                    if ((i + 1) % 10 == 0 || i == totalElements - 1)
                    {
                        TaskRegistry.SetProgress(cmd.TaskId,
                            (int)((i + 1) / (double)totalElements * 100),
                            $"Setting parameter {i + 1}/{totalElements}");
                    }

                    var element = doc.GetElement(eid);
                    if (element is null)
                    {
                        results.Add(new { element_id = eid.IntegerValue, success = false, error = "Element not found" });
                        failCount++;
                        continue;
                    }

                    var parameter = element.LookupParameter(paramName);
                    if (parameter is null)
                    {
                        results.Add(new { element_id = eid.IntegerValue, success = false, error = $"Parameter '{paramName}' not found" });
                        failCount++;
                        continue;
                    }

                    if (parameter.IsReadOnly)
                    {
                        results.Add(new { element_id = eid.IntegerValue, success = false, error = "Parameter is read-only" });
                        failCount++;
                        continue;
                    }

                    bool ok = SetParameterValue(parameter, value);
                    if (ok)
                    {
                        results.Add(new { element_id = eid.IntegerValue, success = true, error = (string?)null });
                        successCount++;
                    }
                    else
                    {
                        results.Add(new { element_id = eid.IntegerValue, success = false, error = "Value type mismatch" });
                        failCount++;
                    }
                }

                t.Commit();
            }

            var result = new
            {
                parameter_name = paramName,
                value = value,
                total = elementIds.Count,
                success_count = successCount,
                fail_count = failCount,
                results = results
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Batch set '{paramName}': {successCount} succeeded, {failCount} failed.").ToJson();
        }

        private List<ElementId>? ResolveElementIds(Document doc, UIDocument uiDoc, Dictionary<string, object> parameters)
        {
            var elementIds = HandlerUtilities.GetIntArrayOrNull(parameters, "element_ids");
            if (elementIds is not null && elementIds.Length > 0)
            {
                return elementIds.Select(id => new ElementId(id)).ToList();
            }

            if (parameters.TryGetValue("category", out var catVal) && catVal is not null)
            {
                string? category = catVal.ToString();
                if (!string.IsNullOrEmpty(category))
                {
                    try
                    {
                        var bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), category);
                        var collector = new FilteredElementCollector(doc)
                            .OfCategory(bic)
                            .WhereElementIsNotElementType();
                        return collector.Select(e => e.Id).Take(500).ToList();
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            if (parameters.TryGetValue("use_selection", out var selVal) && selVal is not null)
            {
                bool useSelection = Convert.ToBoolean(selVal);
                if (useSelection)
                {
                    var selectedIds = uiDoc.Selection.GetElementIds();
                    if (selectedIds.Count > 0)
                        return selectedIds.ToList();
                    return new List<ElementId>();
                }
            }

            return null;
        }

        private bool SetParameterValue(Parameter p, object value)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        if (value is null) return false;
                        p.Set(Convert.ToDouble(value));
                        return true;
                    case StorageType.Integer:
                        if (value is null) return false;
                        p.Set(Convert.ToInt32(value));
                        return true;
                    case StorageType.String:
                        p.Set(value?.ToString() ?? "");
                        return true;
                    case StorageType.ElementId:
                        if (value is null) return false;
                        p.Set(new ElementId(Convert.ToInt32(value)));
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
