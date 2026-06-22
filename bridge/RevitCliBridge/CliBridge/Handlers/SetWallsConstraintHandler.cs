using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class SetWallsConstraintHandler : DocumentCommandBase
    {
        public override string CommandName => "set_walls_constraint";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? topLevelId = HandlerUtilities.GetIntOrNull(parameters, "top_level_id");
            int? baseLevelId = HandlerUtilities.GetIntOrNull(parameters, "base_level_id");

            if (topLevelId is null && baseLevelId is null)
                return CommandResponse.Error(cmd.TaskId, "At least one of top_level_id or base_level_id is required.").ToJson();

            if (parameters.TryGetValue("walls", out var wallsVal) && wallsVal is not null)
                return HandleBatchWalls(doc, cmd, wallsVal, topLevelId, baseLevelId);

            return HandleAllWalls(doc, cmd, parameters, topLevelId, baseLevelId);
        }

        private string HandleBatchWalls(Document doc, QueuedCommand cmd, object wallsVal, int? topLevelId, int? baseLevelId)
        {
            var wallsList = wallsVal as JArray;
            if (wallsList is null || wallsList.Count == 0)
                return CommandResponse.Error(cmd.TaskId, "Walls array is empty or invalid.").ToJson();

            var wallIds = new List<int>();
            foreach (var wallObj in wallsList)
            {
                if (wallObj is JValue jVal && jVal.Type == JTokenType.Integer)
                {
                    wallIds.Add(jVal.Value<int>());
                }
                else if (wallObj is JObject jObj)
                {
                    int? wallId = HandlerUtilities.GetIntOrNull(jObj.ToObject<Dictionary<string, object>>(), "wall_id");
                    if (wallId is null)
                        return CommandResponse.Error(cmd.TaskId, "Invalid wall entry format.").ToJson();
                    wallIds.Add(wallId.Value);
                }
                else
                {
                    return CommandResponse.Error(cmd.TaskId, "Invalid wall entry format.").ToJson();
                }
            }

            var resultPairs = new List<object>();
            int totalWalls = wallIds.Count;

            using (Transaction t = new Transaction(doc, "CLI Set Walls Constraint"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                for (int i = 0; i < wallIds.Count; i++)
                {
                    var wallId = wallIds[i];

                    if ((i + 1) % 10 == 0 || i == totalWalls - 1)
                    {
                        TaskRegistry.SetProgress(cmd.TaskId,
                            (int)((i + 1) / (double)totalWalls * 100),
                            $"Setting constraint {i + 1}/{totalWalls}");
                    }

                    var wall = doc.GetElement(new ElementId(wallId)) as Wall;
                    if (wall is null)
                    {
                        resultPairs.Add(new object[] { wallId, false });
                        continue;
                    }

                    bool success = true;

                    if (baseLevelId is not null)
                    {
                        var baseParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                        if (baseParam is null || baseParam.IsReadOnly || !baseParam.Set(new ElementId(baseLevelId.Value)))
                            success = false;
                    }

                    if (topLevelId is not null && success)
                    {
                        var topParam = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                        if (topParam is null || !topParam.Set(new ElementId(topLevelId.Value)))
                            success = false;
                    }

                    resultPairs.Add(new object[] { wallId, success });
                }

                t.Commit();
            }

            var result = BuildResult(resultPairs, topLevelId, baseLevelId);

            return CommandResponse.Success(cmd.TaskId, result,
                $"Processed {resultPairs.Count} wall constraints.").ToJson();
        }

        private string HandleAllWalls(Document doc, QueuedCommand cmd, Dictionary<string, object> parameters, int? topLevelId, int? baseLevelId)
        {
            string? category = parameters.TryGetValue("category", out var catVal) ? catVal?.ToString() : null;

            var filter = new ElementClassFilter(typeof(Wall));
            var collector = new FilteredElementCollector(doc).WherePasses(filter);

            if (!string.IsNullOrEmpty(category))
            {
                var catFilter = new ElementCategoryFilter(GetBuiltInCategory(category));
                collector = collector.WherePasses(catFilter);
            }

            var walls = collector.ToElements();
            var resultPairs = new List<object>();
            int totalWalls = walls.Count;
            int processedCount = 0;

            using (Transaction t = new Transaction(doc, "CLI Set All Walls Constraint"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                foreach (Element elem in walls)
                {
                    var wall = elem as Wall;
                    if (wall is null) continue;

                    processedCount++;
                    if (processedCount % 10 == 0 || processedCount == totalWalls)
                    {
                        TaskRegistry.SetProgress(cmd.TaskId,
                            (int)(processedCount / (double)totalWalls * 100),
                            $"Setting constraint {processedCount}/{totalWalls}");
                    }

                    bool success = true;

                    if (baseLevelId is not null)
                    {
                        var baseParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                        if (baseParam is null || baseParam.IsReadOnly || !baseParam.Set(new ElementId(baseLevelId.Value)))
                            success = false;
                    }

                    if (topLevelId is not null && success)
                    {
                        var topParam = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                        if (topParam is null || !topParam.Set(new ElementId(topLevelId.Value)))
                            success = false;
                    }

                    resultPairs.Add(new object[] { wall.Id.IntegerValue, success });
                }

                t.Commit();
            }

            var result = BuildResult(resultPairs, topLevelId, baseLevelId);

            var messages = new List<string>();
            if (baseLevelId is not null) messages.Add($"base constraint to level {baseLevelId}");
            if (topLevelId is not null) messages.Add($"top constraint to level {topLevelId}");

            return CommandResponse.Success(cmd.TaskId, result,
                $"Set {string.Join(" and ", messages)} for {resultPairs.Count} walls.").ToJson();
        }

        private object BuildResult(List<object> resultPairs, int? topLevelId, int? baseLevelId)
        {
            var result = new Dictionary<string, object>
            {
                ["results"] = resultPairs
            };

            if (topLevelId is not null)
                result["top_level_id"] = topLevelId.Value;

            if (baseLevelId is not null)
                result["base_level_id"] = baseLevelId.Value;

            return result;
        }

        private BuiltInCategory GetBuiltInCategory(string category)
        {
            return category switch
            {
                "OST_Walls" => BuiltInCategory.OST_Walls,
                "OST_CurtainWallPanels" => BuiltInCategory.OST_CurtainWallPanels,
                _ => BuiltInCategory.OST_Walls
            };
        }
    }
}
