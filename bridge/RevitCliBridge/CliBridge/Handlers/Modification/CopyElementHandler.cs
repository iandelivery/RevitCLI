using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Modification
{
    public class CopyElementHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["translation_x"] = UnitKinds.Length,
                ["translation_y"] = UnitKinds.Length,
                ["translation_z"] = UnitKinds.Length
            };

        public override string CommandName => "copy_element";
        public override string Description => "Copies an element by a translation vector (dx, dy, dz)";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Element ID to copy" },
            new CommandParamSchema { Name = "dx", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "Translation X (request length unit, default mm)" },
            new CommandParamSchema { Name = "dy", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "Translation Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "dz", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Translation Z (request length unit, default mm)", Default = 0 }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"copy_element\", \"parameters\": { \"element_id\": 12345, \"dx\": 5000, \"dy\": 0 } }",
            "{ \"command\": \"copy_element\", \"parameters\": { \"element_id\": 12345, \"dx\": 3000, \"dy\": 2000, \"dz\": 0 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? elementId = HandlerUtilities.GetIntOrNull(parameters, "element_id");
            double? dx = HandlerUtilities.GetDoubleOrNull(parameters, "dx");
            double? dy = HandlerUtilities.GetDoubleOrNull(parameters, "dy");
            double? dz = HandlerUtilities.GetDoubleOrNull(parameters, "dz");

            if (elementId is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: element_id.").ToJson();

            if (dx is null || dy is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameters: dx and dy.").ToJson();

            var element = doc.GetElement(ElementIdCompat.FromValue(elementId.Value));
            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId.Value} not found.").ToJson();

            using (var t = new DryRunTransaction(doc, "CLI Copy Element", cmd.DryRun))
            {
                t.ConfigureFailureHandling();

                var translation = new XYZ(dx.Value, dy.Value, dz ?? 0);
                var newElementIds = ElementTransformUtils.CopyElement(doc, element.Id, translation);

                t.Commit();

                var newId = newElementIds.FirstOrDefault();

                var result = new
                {
                    original_element_id = elementId.Value,
                    new_element_id = newId is { } copiedId ? ElementIdCompat.ValueOf(copiedId) : (long?)null,
                    translation_x = dx.Value,
                    translation_y = dy.Value,
                    translation_z = dz ?? 0
                };

                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Element copied successfully.", t.ResolvedFailures).ToJson();
            }
        }
    }
}
