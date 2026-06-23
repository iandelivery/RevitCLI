using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    public class DeleteElementHandler : DocumentCommandBase
    {
        public override string CommandName => "delete_element";
        public override string Description => "Deletes an element from the document";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Element ID to delete" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"delete_element\", \"parameters\": { \"element_id\": 12345 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            if (!parameters.TryGetValue("element_id", out var idVal) || idVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'element_id' parameter.").ToJson();

            int elementId = Convert.ToInt32(idVal);
            var element = doc.GetElement(new ElementId(elementId));

            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId} not found.").ToJson();

            using (Transaction t = new Transaction(doc, "CLI Delete Element"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                doc.Delete(element.Id);
                t.Commit();
            }

            return CommandResponse.Success(cmd.TaskId,
                new { element_id = elementId }, "Element deleted successfully.").ToJson();
        }
    }
}