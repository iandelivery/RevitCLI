using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class GetElementByIdHandler : DocumentCommandBase
    {
        public override string CommandName => "get_element_by_id";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            if (!parameters.TryGetValue("element_id", out var idVal) || idVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'element_id' parameter.").ToJson();

            int elementId = Convert.ToInt32(idVal);
            var element = doc.GetElement(new ElementId(elementId));

            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId} not found.").ToJson();

            var elementInfo = new
            {
                id = element.Id.IntegerValue,
                name = element.Name,
                category = element.Category?.Name,
                class_type = element.GetType().Name,
                unique_id = element.UniqueId
            };

            return CommandResponse.Success(cmd.TaskId, elementInfo, "Element retrieved.").ToJson();
        }
    }
}