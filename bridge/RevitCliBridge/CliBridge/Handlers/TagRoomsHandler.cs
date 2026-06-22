using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitCliBridge.Handlers
{
    public class TagRoomsHandler : DocumentCommandBase
    {
        public override string CommandName => "tag_rooms";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            var uiDoc = app.ActiveUIDocument;
            if (uiDoc is null)
                return CommandResponse.Error(cmd.TaskId, "No active UI document.").ToJson();

            int? viewId = HandlerUtilities.GetIntOrNull(parameters, "view_id");
            var roomIds = HandlerUtilities.GetIntArrayOrNull(parameters, "room_ids");
            int? tagTypeId = HandlerUtilities.GetIntOrNull(parameters, "tag_type_id");

            var activeView = viewId.HasValue
                ? doc.GetElement(new ElementId(viewId.Value)) as View
                : doc.ActiveView;

            if (activeView is null)
                return CommandResponse.Error(cmd.TaskId, "No active view or specified view not found.").ToJson();

            var rooms = ResolveRooms(doc, roomIds);
            if (rooms.Count == 0)
                return CommandResponse.Error(cmd.TaskId, "No rooms found to tag.").ToJson();

            FamilySymbol? tagType = null;
            if (tagTypeId.HasValue)
            {
                tagType = doc.GetElement(new ElementId(tagTypeId.Value)) as FamilySymbol;
            }

            if (tagType is null)
            {
                tagType = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_RoomTags)
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();
            }

            if (tagType is null)
                return CommandResponse.Error(cmd.TaskId, "No room tag type found in document.").ToJson();

            var results = new List<object>();
            int successCount = 0;
            int failCount = 0;

            using (Transaction t = new Transaction(doc, "CLI Tag Rooms"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                if (!tagType.IsActive)
                    tagType.Activate();

                foreach (var room in rooms)
                {
                    try
                    {
                        var location = room.Location as LocationPoint;
                        if (location is null)
                        {
                            results.Add(new { room_id = room.Id.IntegerValue, success = false, error = "Room has no location point" });
                            failCount++;
                            continue;
                        }

                        var tag = IndependentTag.Create(doc, tagType.Id, activeView.Id,
                            new Reference(room), false, TagOrientation.Horizontal, location.Point);

                        results.Add(new { room_id = room.Id.IntegerValue, tag_id = tag.Id.IntegerValue, success = true });
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { room_id = room.Id.IntegerValue, success = false, error = ex.Message });
                        failCount++;
                    }
                }

                t.Commit();
            }

            var result = new
            {
                view_id = activeView.Id.IntegerValue,
                tag_type_id = tagType.Id.IntegerValue,
                total = rooms.Count,
                success_count = successCount,
                fail_count = failCount,
                results = results
            };

            return CommandResponse.Success(cmd.TaskId, result,
                $"Tag rooms: {successCount} succeeded, {failCount} failed.").ToJson();
        }

        private List<Room> ResolveRooms(Document doc, int[]? roomIds)
        {
            if (roomIds is not null && roomIds.Length > 0)
            {
                var rooms = new List<Room>();
                foreach (var id in roomIds)
                {
                    var room = doc.GetElement(new ElementId(id)) as Room;
                    if (room is not null && room.Area > 0)
                        rooms.Add(room);
                }
                return rooms;
            }

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .Take(500)
                .ToList();
        }
    }
}
