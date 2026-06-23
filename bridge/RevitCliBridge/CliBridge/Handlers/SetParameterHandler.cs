using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    public class SetParameterHandler : DocumentCommandBase
    {
        public override string CommandName => "set_parameter";
        public override string Description => "Sets a parameter value on an element by parameter name";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Element ID" },
            new CommandParamSchema { Name = "parameter_name", Type = "string", Required = true, Description = "Parameter name to set" },
            new CommandParamSchema { Name = "value", Type = "string", Required = true, Description = "Value to set (auto-converted to match parameter storage type)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"set_parameter\", \"parameters\": { \"element_id\": 12345, \"parameter_name\": \"Comments\", \"value\": \"Approved\" } }",
            "{ \"command\": \"set_parameter\", \"parameters\": { \"element_id\": 12345, \"parameter_name\": \"Mark\", \"value\": \"A-1\" } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            if (!parameters.TryGetValue("element_id", out var idVal) || idVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'element_id' parameter.").ToJson();

            if (!parameters.TryGetValue("parameter_name", out var nameVal) || nameVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'parameter_name' parameter.").ToJson();

            if (!parameters.TryGetValue("value", out var valVal))
                return CommandResponse.Error(cmd.TaskId, "Missing 'value' parameter.").ToJson();

            int elementId = Convert.ToInt32(idVal);
            string paramName = nameVal.ToString() ?? "";
            object value = valVal;

            var element = doc.GetElement(new ElementId(elementId));
            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId} not found.").ToJson();

            var parameter = element.LookupParameter(paramName);
            if (parameter is null)
                return CommandResponse.Error(cmd.TaskId, $"Parameter '{paramName}' not found on element.").ToJson();

            using (Transaction t = new Transaction(doc, "CLI Set Parameter"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                bool success = SetParameterValue(parameter, value);
                if (!success)
                {
                    t.RollBack();
                    return CommandResponse.Error(cmd.TaskId,
                        $"Failed to set parameter '{paramName}'. Value type may not match.").ToJson();
                }

                t.Commit();
            }

            return CommandResponse.Success(cmd.TaskId,
                new { element_id = elementId, parameter_name = paramName, value = value },
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