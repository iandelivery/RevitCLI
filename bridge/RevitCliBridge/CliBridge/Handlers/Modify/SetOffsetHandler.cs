using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class SetOffsetHandler : DocumentCommandBase
    {
        public override string CommandName => "set_offset";
        public override string Description => "Sets base and/or top offset on a wall or family instance";
        public override string Category => "Modify";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Element ID (wall or family instance)" },
            new CommandParamSchema { Name = "base_offset", Type = "double", Required = false, Description = "Base offset in millimeters" },
            new CommandParamSchema { Name = "top_offset", Type = "double", Required = false, Description = "Top offset in millimeters" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"set_offset\", \"parameters\": { \"element_id\": 12345, \"base_offset\": -50 } }",
            "{ \"command\": \"set_offset\", \"parameters\": { \"element_id\": 12345, \"base_offset\": 0, \"top_offset\": 500 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? elementId = HandlerUtilities.GetIntOrNull(parameters, "element_id");
            double? baseOffset = HandlerUtilities.GetDoubleOrNull(parameters, "base_offset");
            double? topOffset = HandlerUtilities.GetDoubleOrNull(parameters, "top_offset");

            if (elementId is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: element_id.").ToJson();

            if (baseOffset is null && topOffset is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: at least one of base_offset or top_offset.").ToJson();

            var element = doc.GetElement(new ElementId(elementId.Value));
            if (element is null)
                return CommandResponse.Error(cmd.TaskId, $"Element with ID {elementId.Value} not found.").ToJson();

            var baseOffsetParam = GetBaseOffsetParameter(element);
            var topOffsetParam = GetTopOffsetParameter(element);

            if (baseOffsetParam is null && baseOffset is not null)
                return CommandResponse.Error(cmd.TaskId, $"Element does not support base offset.").ToJson();

            if (topOffsetParam is null && topOffset is not null)
                return CommandResponse.Error(cmd.TaskId, $"Element does not support top offset.").ToJson();

            using (Transaction t = new Transaction(doc, "CLI Set Offset"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                double? oldBaseOffset = null;
                double? oldTopOffset = null;

                if (baseOffsetParam is not null && baseOffset is not null)
                {
                    oldBaseOffset = baseOffsetParam.AsDouble();
                    baseOffsetParam.Set(baseOffset.Value.MillimeterToFeet());
                }

                if (topOffsetParam is not null && topOffset is not null)
                {
                    oldTopOffset = topOffsetParam.AsDouble();
                    topOffsetParam.Set(topOffset.Value.MillimeterToFeet());
                }

                t.Commit();

                var result = new Dictionary<string, object>
                {
                    ["element_id"] = elementId.Value
                };

                if (baseOffset is not null)
                {
                    result["base_offset"] = baseOffset.Value;
                    result["old_base_offset"] = oldBaseOffset?.FeetToMillimeter();
                }

                if (topOffset is not null)
                {
                    result["top_offset"] = topOffset.Value;
                    result["old_top_offset"] = oldTopOffset?.FeetToMillimeter();
                }

                return CommandResponse.Success(cmd.TaskId, result, "Offset set successfully.").ToJson();
            }
        }

        private Parameter? GetBaseOffsetParameter(Element element)
        {
            if (element is Wall wall)
                return wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);

            if (element is FamilyInstance familyInstance)
            {
                var param = familyInstance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                if (param is not null && !param.IsReadOnly)
                    return param;
            }

            return element.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
        }

        private Parameter? GetTopOffsetParameter(Element element)
        {
            if (element is Wall wall)
                return wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);

            if (element is FamilyInstance familyInstance)
            {
                var param = familyInstance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                if (param is not null && !param.IsReadOnly)
                    return param;
            }

            return element.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
        }
    }
}
