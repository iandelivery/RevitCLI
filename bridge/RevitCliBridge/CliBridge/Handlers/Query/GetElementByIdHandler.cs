using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Query
{
    public class GetElementByIdHandler : DocumentCommandBase
    {
        public override string CommandName => "get_element_by_id";
        public override string Description => "Retrieves an element by its ID from the active document or a linked document";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Element ID to look up (within the target document)" },
            new CommandParamSchema { Name = "link", Type = "string", Required = false, Description = "Linked document to query: RevitLinkInstance id or exact link name (see get_links). Default: the host document." }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_element_by_id\", \"parameters\": { \"element_id\": 12345 } }",
            "{ \"command\": \"get_element_by_id\", \"parameters\": { \"element_id\": 100123, \"link\": 184324 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            if (!parameters.TryGetValue("element_id", out var idVal) || idVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'element_id' parameter.").ToJson();

            var target = HandlerUtilities.TryResolveTargetDocument(doc, parameters, out var linkLabel);
            if (target is null)
                return CommandResponse.Error(cmd.TaskId, $"Linked document not loaded or not found: {linkLabel}").ToJson();

            int elementId = Convert.ToInt32(idVal);
            var element = target.GetElement(new ElementId(elementId));

            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId} not found.").ToJson();

            var selector = HandlerUtilities.ResolveLinkSelector(
                HandlerUtilities.GetStringOrNull(parameters, "link"));
            var instance = selector is null ? null : GetElementsHandler.FindInstance(doc, selector);

            var elementInfo = new
            {
                id = element.Id.IntegerValue,
                name = element.Name,
                category = element.Category?.Name,
                class_type = element.GetType().Name,
                unique_id = element.UniqueId,
                link_id = instance?.Id.IntegerValue,
                link_name = instance?.Name
            };

            return CommandResponse.Success(cmd.TaskId, elementInfo, "Element retrieved.").ToJson();
        }
    }
}
