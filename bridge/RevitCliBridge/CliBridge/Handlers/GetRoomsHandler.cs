using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class GetRoomsHandler : DocumentCommandBase
    {
        public override string CommandName => "get_rooms";
        public override string Description => "Retrieves rooms, optionally filtered by level";
        public override string Category => "Query";

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "level_id", Type = "int", Required = false, Description = "Level element ID to filter rooms by" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_rooms\", \"parameters\": {} }",
            "{ \"command\": \"get_rooms\", \"parameters\": { \"level_id\": 3001 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? levelId = HandlerUtilities.GetIntOrNull(parameters, "level_id");

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();

            if (levelId.HasValue)
            {
                var level = doc.GetElement(new ElementId(levelId.Value)) as Level;
                if (level is not null)
                    collector = collector.OfCategory(BuiltInCategory.OST_Rooms);
            }

            var rooms = collector
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .Where(r => !levelId.HasValue || r.LevelId.IntegerValue == levelId.Value)
                .Select(r => new
                {
                    id = r.Id.IntegerValue,
                    name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? r.Name,
                    number = r.Number,
                    level_id = r.LevelId.IntegerValue,
                    area = r.Area
                })
                .ToList();

            var result = new
            {
                count = rooms.Count,
                rooms = rooms
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Retrieved {rooms.Count} rooms.").ToJson();
        }
    }
}
