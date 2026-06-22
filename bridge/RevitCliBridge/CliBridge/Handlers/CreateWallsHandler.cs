using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCliBridge.Handlers;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Models;

namespace RevitCliBridge.Handlers
{
    public class CreateWallsHandler : DocumentCommandBase
    {
        public override string CommandName => "create_walls";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            if (!parameters.TryGetValue("walls", out var wallsVal) || wallsVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: walls.").ToJson();

            var wallsList = wallsVal as JArray;
            if (wallsList is null || wallsList.Count == 0)
                return CommandResponse.Error(cmd.TaskId, "Walls array is empty or invalid.").ToJson();

            var wallEntries = new List<WallEntry>();
            foreach (var wallObj in wallsList)
            {
                var wallDict = wallObj as JObject;
                if (wallDict is null)
                    return CommandResponse.Error(cmd.TaskId, "Invalid wall entry format.").ToJson();

                double? startX = wallDict.TryGetValue("start_x", out var sx) ? (double?)sx : null;
                double? startY = wallDict.TryGetValue("start_y", out var sy) ? (double?)sy : null;
                double? endX = wallDict.TryGetValue("end_x", out var ex) ? (double?)ex : null;
                double? endY = wallDict.TryGetValue("end_y", out var ey) ? (double?)ey : null;
                int? levelId = wallDict.TryGetValue("level_id", out var lid) ? (int?)lid : null;
                double height = wallDict.TryGetValue("height", out var h) ? (double)h : 3000.0;

                if (startX is null || startY is null || endX is null || endY is null || levelId is null)
                    return CommandResponse.Error(cmd.TaskId,
                        $"Missing required fields in wall entry. Required: start_x, start_y, end_x, end_y, level_id.").ToJson();

                wallEntries.Add(new WallEntry
                {
                    StartX = startX.Value,
                    StartY = startY.Value,
                    EndX = endX.Value,
                    EndY = endY.Value,
                    LevelId = levelId.Value,
                    Height = height
                });
            }

            var resultPairs = new List<object>();
            int totalWalls = wallEntries.Count;

            using (Transaction t = new Transaction(doc, "CLI Create Walls"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                for (int i = 0; i < wallEntries.Count; i++)
                {
                    var entry = wallEntries[i];

                    TaskRegistry.SetProgress(cmd.TaskId,
                        (int)((i + 1) / (double)totalWalls * 100),
                        $"Creating wall {i + 1}/{totalWalls}");

                    var start = new XYZ(entry.StartX.MillimeterToFeet(), entry.StartY.MillimeterToFeet(), 0);
                    var end = new XYZ(entry.EndX.MillimeterToFeet(), entry.EndY.MillimeterToFeet(), 0);

                    var wall = Wall.Create(
                        doc,
                        Line.CreateBound(start, end),
                        new ElementId(entry.LevelId),
                        false);

                    resultPairs.Add(new object[] { wall.Id.IntegerValue, entry.LevelId });
                }

                t.Commit();
            }

            return CommandResponse.Success(cmd.TaskId, resultPairs,
                $"Created {resultPairs.Count} walls.").ToJson();
        }
    }
}
