using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class CreateRoomHandler : DocumentCommandBase
    {
        public override string CommandName => "create_room";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? levelId = HandlerUtilities.GetIntOrNull(parameters, "level_id");
            double? x = HandlerUtilities.GetDoubleOrNull(parameters, "x");
            double? y = HandlerUtilities.GetDoubleOrNull(parameters, "y");
            string? roomName = HandlerUtilities.GetStringOrNull(parameters, "name");
            string? roomNumber = HandlerUtilities.GetStringOrNull(parameters, "number");

            if (!levelId.HasValue)
                return CommandResponse.Error(cmd.TaskId, "Missing 'level_id' parameter.").ToJson();

            if (!x.HasValue || !y.HasValue)
                return CommandResponse.Error(cmd.TaskId, "Missing 'x' and/or 'y' parameters.").ToJson();

            var level = doc.GetElement(new ElementId(levelId.Value)) as Level;
            if (level is null)
                return CommandResponse.Error(cmd.TaskId, $"Level with ID {levelId.Value} not found.").ToJson();

            var phase = GetLastPhase(doc);
            if (phase is null)
                return CommandResponse.Error(cmd.TaskId, "No phase found in document.").ToJson();

            Room? room = null;

            using (Transaction t = new Transaction(doc, "CLI Create Room"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                try
                {
                    var point = new XYZ(x.Value.MillimeterToFeet(), y.Value.MillimeterToFeet(), level.Elevation);
                    room = doc.Create.NewRoom(phase);

                    if (room is not null)
                    {
                        if (!string.IsNullOrEmpty(roomNumber))
                            room.Number = roomNumber;

                        if (!string.IsNullOrEmpty(roomName))
                        {
                            var nameParam = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                            if (nameParam is not null && !nameParam.IsReadOnly)
                                nameParam.Set(roomName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return CommandResponse.Error(cmd.TaskId, $"Failed to create room: {ex.Message}").ToJson();
                }

                t.Commit();
            }

            if (room is null)
                return CommandResponse.Error(cmd.TaskId, "Failed to create room.").ToJson();

            var result = new
            {
                element_id = room.Id.IntegerValue,
                name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name,
                number = room.Number,
                level_id = levelId.Value
            };

            return CommandResponse.Success(cmd.TaskId, result, "Room created successfully.").ToJson();
        }

        private Phase? GetLastPhase(Document doc)
        {
            var phases = new FilteredElementCollector(doc)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .OrderBy(p => p.Id.IntegerValue)
                .ToList();

            return phases.LastOrDefault();
        }
    }
}
