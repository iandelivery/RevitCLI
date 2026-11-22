using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers.Creation
{
    public class CreateDoorHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["location_x"] = UnitKinds.Length,
                ["location_y"] = UnitKinds.Length
            };

        public override string CommandName => "create_door";
        public override string Description => "Creates a door instance on a wall at a specified location";
        public override string Category => "Create";
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "wall_id", Type = "int", Required = true, Description = "Host wall element ID" },
            new CommandParamSchema { Name = "family_type_id", Type = "int", Required = true, Description = "FamilySymbol element ID for the door type" },
            new CommandParamSchema { Name = "location_x", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "Insertion point X (request length unit, default mm)" },
            new CommandParamSchema { Name = "location_y", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "Insertion point Y (request length unit, default mm)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_door\", \"parameters\": { \"wall_id\": 12345, \"family_type_id\": 67890, \"location_x\": 2500, \"location_y\": 0 } }"
        };

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
            double locX = Convert.ToDouble(locXVal);
            double locY = Convert.ToDouble(locYVal);

            var wall = doc.GetElement(new ElementId(wallId)) as Wall;
            if (wall is null)
                return CommandResponse.Error(cmd.TaskId, $"Wall with ID {wallId} not found.").ToJson();

            var doorType = doc.GetElement(new ElementId(familyTypeId)) as FamilySymbol;
            if (doorType is null)
                return CommandResponse.Error(cmd.TaskId, $"FamilySymbol with ID {familyTypeId} not found.").ToJson();

            using (var t = new DryRunTransaction(doc, "CLI Create Door", cmd.DryRun))
            {
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
                    location_x = locX,
                    location_y = locY
                };

                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Door created successfully.", t.ResolvedFailures).ToJson();
            }
        }
    }
}