using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class SetWallConstraintHandler : DocumentCommandBase
    {
        public override string CommandName => "set_wall_constraint";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? wallId = HandlerUtilities.GetIntOrNull(parameters, "wall_id");
            int? topLevelId = HandlerUtilities.GetIntOrNull(parameters, "top_level_id");
            int? baseLevelId = HandlerUtilities.GetIntOrNull(parameters, "base_level_id");

            if (wallId is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: wall_id.").ToJson();

            if (topLevelId is null && baseLevelId is null)
                return CommandResponse.Error(cmd.TaskId, "At least one of top_level_id or base_level_id is required.").ToJson();

            var wall = doc.GetElement(new ElementId(wallId.Value)) as Wall;
            if (wall is null)
                return CommandResponse.Error(cmd.TaskId, $"Wall with ID {wallId} not found.").ToJson();

            var result = new Dictionary<string, object>();
            result["wall_id"] = wallId.Value;

            using (Transaction t = new Transaction(doc, "CLI Set Wall Constraint"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                if (baseLevelId is not null)
                {
                    var baseParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    if (baseParam is null || baseParam.IsReadOnly)
                    {
                        t.RollBack();
                        return CommandResponse.Error(cmd.TaskId, "WALL_BASE_CONSTRAINT parameter not found or is read-only.").ToJson();
                    }

                    if (!baseParam.Set(new ElementId(baseLevelId.Value)))
                    {
                        t.RollBack();
                        return CommandResponse.Error(cmd.TaskId, $"Failed to set base constraint to level {baseLevelId}.").ToJson();
                    }

                    result["base_level_id"] = baseLevelId.Value;
                }

                if (topLevelId is not null)
                {
                    var topParam = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                    if (topParam is null)
                    {
                        t.RollBack();
                        return CommandResponse.Error(cmd.TaskId, "WALL_HEIGHT_TYPE parameter not found on this wall.").ToJson();
                    }

                    if (!topParam.Set(new ElementId(topLevelId.Value)))
                    {
                        t.RollBack();
                        return CommandResponse.Error(cmd.TaskId, $"Failed to set top constraint to level {topLevelId}.").ToJson();
                    }

                    result["top_level_id"] = topLevelId.Value;
                }

                t.Commit();
            }

            var messages = new List<string>();
            if (baseLevelId is not null) messages.Add($"base constraint to level {baseLevelId}");
            if (topLevelId is not null) messages.Add($"top constraint to level {topLevelId}");

            return CommandResponse.Success(cmd.TaskId, result,
                $"Wall {string.Join(" and ", messages)}.").ToJson();
        }
    }
}
