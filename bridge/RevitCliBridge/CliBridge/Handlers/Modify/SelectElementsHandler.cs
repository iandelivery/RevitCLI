using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    /// <summary>
    /// Gets or sets the selected elements in Revit.
    /// Supports retrieving current selection or setting selection by element ID list.
    /// </summary>
    public class SelectElementsHandler : DocumentCommandBase
    {
        public override string CommandName => "select_elements";
        public override string Description => "Gets or sets the selected elements in Revit";
        public override string Category => "UI";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_ids", Type = "int[]", Required = false, Description = "Element IDs to select (omit to get current selection)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"select_elements\", \"parameters\": {} }",
            "{ \"command\": \"select_elements\", \"parameters\": { \"element_ids\": [123, 456, 789] } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var uiDoc = app.ActiveUIDocument;
            if (uiDoc is null)
                return CommandResponse.Error(cmd.TaskId, "No active document.").ToJson();

            var elementIds = HandlerUtilities.GetIntArrayOrNull(parameters, "element_ids");

            if (elementIds is not null && elementIds.Length > 0)
            {
                return SetSelection(uiDoc, cmd, elementIds);
            }
            else
            {
                return GetSelection(uiDoc, cmd);
            }
        }

        private string GetSelection(UIDocument uiDoc, QueuedCommand cmd)
        {
            var selection = uiDoc.Selection;
            var selectedIds = selection.GetElementIds();

            if (selectedIds.Count == 0)
            {
                var emptyResult = new
                {
                    count = 0,
                    elements = Array.Empty<object>()
                };
                return CommandResponse.Success(cmd.TaskId, emptyResult, "No elements selected.").ToJson();
            }

            var doc = uiDoc.Document;
            var elements = new List<object>();

            foreach (var id in selectedIds)
            {
                var element = doc.GetElement(id);
                if (element is not null)
                {
                    elements.Add(new
                    {
                        id = id.IntegerValue,
                        name = element.Name ?? string.Empty,
                        category = element.Category?.Name,
                        class_type = element.GetType().Name
                    });
                }
            }

            var result = new
            {
                count = elements.Count,
                elements = elements
            };

            return CommandResponse.Success(cmd.TaskId, result, $"Retrieved {elements.Count} selected elements.").ToJson();
        }

        private string SetSelection(UIDocument uiDoc, QueuedCommand cmd, int[] elementIds)
        {
            var doc = uiDoc.Document;
            var idsToSelect = new List<ElementId>();

            foreach (var id in elementIds)
            {
                var element = doc.GetElement(new ElementId(id));
                if (element is not null)
                {
                    idsToSelect.Add(element.Id);
                }
            }

            if (idsToSelect.Count == 0)
            {
                return CommandResponse.Error(cmd.TaskId, "No valid elements found to select.").ToJson();
            }

            uiDoc.Selection.SetElementIds(idsToSelect);

            var result = new
            {
                selected_count = idsToSelect.Count,
                element_ids = idsToSelect.Select(id => id.IntegerValue).ToArray()
            };

            return CommandResponse.Success(cmd.TaskId, result, $"Selected {idsToSelect.Count} elements.").ToJson();
        }
    }
}
