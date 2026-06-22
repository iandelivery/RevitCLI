using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    public class CreateDoorHandler : DocumentCommandBase
    {
        public override string CommandName => "create_door";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            if (!parameters.TryGetValue("wall_id", out var wallIdVal) || wallIdVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'wall_id' parameter.").ToJson();

            if (!parameters.TryGetValue("family_type_id", out var familyTypeIdVal) || familyTypeIdVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'family_type_id' parameter.").ToJson();

            if (!parameters.TryGetValue("location_x", out var locXVal) || locXVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'location_x' parameter.").ToJson();

            if (!parameters.TryGetValue("location_y", out var locYVal) || locYVal is null)
                return CommandResponse.Error(cmd.TaskId, "Missing 'location_y' parameter.").ToJson();

            int wallId = Convert.ToInt32(wallIdVal);
            int familyTypeId = Convert.ToInt32(familyTypeIdVal);
            double locX = Convert.ToDouble(locXVal).MillimeterToFeet();
            double locY = Convert.ToDouble(locYVal).MillimeterToFeet();

            var wall = doc.GetElement(new ElementId(wallId)) as Wall;
            if (wall is null)
                return CommandResponse.Error(cmd.TaskId, $"Wall with ID {wallId} not found.").ToJson();

            var doorType = doc.GetElement(new ElementId(familyTypeId)) as FamilySymbol;
            if (doorType is null)
                return CommandResponse.Error(cmd.TaskId, $"FamilySymbol with ID {familyTypeId} not found.").ToJson();

            using (Transaction t = new Transaction(doc, "CLI Create Door"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                if (!doorType.IsActive)
                    doorType.Activate();

                var location = new XYZ(locX, locY, 0);
                var door = doc.Create.NewFamilyInstance(location, doorType, wall, StructuralType.NonStructural);

                t.Commit();

                var result = new
                {
                    element_id = door.Id.IntegerValue,
                    wall_id = wallId,
                    family_type_id = familyTypeId,
                    location_x = locX.FeetToMillimeter(),
                    location_y = locY.FeetToMillimeter()
                };

                return CommandResponse.Success(cmd.TaskId, result, "Door created successfully.").ToJson();
            }
        }
    }
}