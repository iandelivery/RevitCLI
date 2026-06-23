using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    public class CreateWindowHandler : DocumentCommandBase
    {
        public override string CommandName => "create_window";
        public override string Description => "Creates a window instance on a wall at a specified location";
        public override string Category => "Create";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "wall_id", Type = "int", Required = true, Description = "Host wall element ID" },
            new CommandParamSchema { Name = "family_type_id", Type = "int", Required = true, Description = "FamilySymbol element ID for the window type" },
            new CommandParamSchema { Name = "location_x", Type = "double", Required = true, Description = "Insertion point X in millimeters" },
            new CommandParamSchema { Name = "location_y", Type = "double", Required = true, Description = "Insertion point Y in millimeters" },
            new CommandParamSchema { Name = "sill_height", Type = "double", Required = false, Description = "Sill height in millimeters" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_window\", \"parameters\": { \"wall_id\": 12345, \"family_type_id\": 67890, \"location_x\": 2500, \"location_y\": 0, \"sill_height\": 900 } }"
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
            double locX = Convert.ToDouble(locXVal).MillimeterToFeet();
            double locY = Convert.ToDouble(locYVal).MillimeterToFeet();

            double? sillHeight = HandlerUtilities.GetDoubleOrNull(parameters, "sill_height")?.MillimeterToFeet();

            var wall = doc.GetElement(new ElementId(wallId)) as Wall;
            if (wall is null)
                return CommandResponse.Error(cmd.TaskId, $"Wall with ID {wallId} not found.").ToJson();

            var windowType = doc.GetElement(new ElementId(familyTypeId)) as FamilySymbol;
            if (windowType is null)
                return CommandResponse.Error(cmd.TaskId, $"FamilySymbol with ID {familyTypeId} not found.").ToJson();

            using (Transaction t = new Transaction(doc, "CLI Create Window"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                if (!windowType.IsActive)
                    windowType.Activate();

                var location = new XYZ(locX, locY, 0);
                var window = doc.Create.NewFamilyInstance(location, windowType, wall, StructuralType.NonStructural);

                if (sillHeight.HasValue)
                {
                    var sillParam = window.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
                    if (sillParam is not null && !sillParam.IsReadOnly)
                        sillParam.Set(sillHeight.Value);
                }

                t.Commit();

                var result = new
                {
                    element_id = window.Id.IntegerValue,
                    wall_id = wallId,
                    family_type_id = familyTypeId,
                    location_x = locX.FeetToMillimeter(),
                    location_y = locY.FeetToMillimeter(),
                    sill_height = sillHeight?.FeetToMillimeter()
                };

                return CommandResponse.Success(cmd.TaskId, result, "Window created successfully.").ToJson();
            }
        }
    }
}
