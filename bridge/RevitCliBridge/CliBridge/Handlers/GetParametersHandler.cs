using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    public class GetParametersHandler : DocumentCommandBase
    {
        public override string CommandName => "get_parameters";
        public override string Description => "Retrieves all parameters of an element with their values and metadata";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Element ID to get parameters from" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_parameters\", \"parameters\": { \"element_id\": 12345 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            if (!parameters.TryGetValue("element_id", out var idVal) || idVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'element_id' parameter.").ToJson();

            int elementId = Convert.ToInt32(idVal);
            var element = doc.GetElement(new ElementId(elementId));

            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId} not found.").ToJson();

            var paramList = new List<object>();

            foreach (Parameter p in element.Parameters)
            {
                if (p.Definition.Name is null) continue;

                var entry = new
                {
                    name = p.Definition.Name,
                    value = GetParameterValue(p),
                    storage_type = p.StorageType.ToString(),
                    is_read_only = p.IsReadOnly,
                    is_shared = p.IsShared
                };
                paramList.Add(entry);
            }

            var result = new
            {
                element_id = elementId,
                element_name = element.Name,
                parameters = paramList
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Retrieved {paramList.Count} parameters.").ToJson();
        }

        private object? GetParameterValue(Parameter p)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        return p.AsDouble();
                    case StorageType.Integer:
                        return p.AsInteger();
                    case StorageType.String:
                        return p.AsString();
                    case StorageType.ElementId:
                        return p.AsElementId().IntegerValue;
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}