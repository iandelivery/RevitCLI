using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    public class SetParameterByIdHandler : DocumentCommandBase
    {
        public override string CommandName => "set_parameter_by_id";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            if (!parameters.TryGetValue("element_id", out var idVal) || idVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'element_id' parameter.").ToJson();

            if (!parameters.TryGetValue("built_in_parameter", out var bipVal) || bipVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'built_in_parameter' parameter.").ToJson();

            if (!parameters.TryGetValue("value", out var valVal))
                return CommandResponse.Error(cmd.TaskId, "Missing 'value' parameter.").ToJson();

            int elementId = Convert.ToInt32(idVal);
            BuiltInCategory bip;
            try
            {
                bip = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), valVal.ToString() ?? "");
            }
            catch
            {
                return CommandResponse.Error(cmd.TaskId, $"Invalid BuiltInParameter value: {valVal}").ToJson();
            }

            var element = doc.GetElement(new ElementId(elementId));
            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId} not found.").ToJson();

            var parameter = element.get_Parameter((BuiltInParameter)bip);
            if (parameter is null)
                return CommandResponse.Error(cmd.TaskId, $"BuiltInParameter '{bip}' not found on element.").ToJson();

            using (Transaction t = new Transaction(doc, "CLI Set Parameter By ID"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                bool success = SetParameterValue(parameter, valVal);
                if (!success)
                {
                    t.RollBack();
                    return CommandResponse.Error(cmd.TaskId,
                        $"Failed to set parameter '{bip}'. Value type may not match.").ToJson();
                }

                t.Commit();
            }

            return CommandResponse.Success(cmd.TaskId,
                new { element_id = elementId, parameter = bip.ToString(), value = valVal },
                "Parameter set successfully.").ToJson();
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