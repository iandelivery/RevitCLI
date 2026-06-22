using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers
{
    public class CreateFamilyInstanceHandler : DocumentCommandBase
    {
        public override string CommandName => "create_family_instance";
        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {

            int? symbolId = HandlerUtilities.GetIntOrNull(parameters, "symbol_id");
            int? levelId = HandlerUtilities.GetIntOrNull(parameters, "level_id");
            double? x = HandlerUtilities.GetDoubleOrNull(parameters, "x");
            double? y = HandlerUtilities.GetDoubleOrNull(parameters, "y");
            double? z = HandlerUtilities.GetDoubleOrNull(parameters, "z");
            string? structuralTypeStr = HandlerUtilities.GetStringOrNull(parameters, "structural_type");

            if (symbolId is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: symbol_id.").ToJson();

            if (levelId is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameter: level_id.").ToJson();

            if (x is null || y is null)
                return CommandResponse.Error(cmd.TaskId, "Missing required parameters: x and y.").ToJson();

            var symbol = doc.GetElement(new ElementId(symbolId.Value)) as FamilySymbol;
            if (symbol is null)
                return CommandResponse.Error(cmd.TaskId, $"FamilySymbol with ID {symbolId.Value} not found.").ToJson();

            var level = doc.GetElement(new ElementId(levelId.Value)) as Level;
            if (level is null)
                return CommandResponse.Error(cmd.TaskId, $"Level with ID {levelId.Value} not found.").ToJson();

            StructuralType structuralType = StructuralType.NonStructural;
            if (!string.IsNullOrEmpty(structuralTypeStr))
            {
                if (!Enum.TryParse(structuralTypeStr, out structuralType))
                    return CommandResponse.Error(cmd.TaskId,
                        $"Invalid StructuralType: {structuralTypeStr}. Valid values: {string.Join(", ", Enum.GetNames(typeof(StructuralType)))}").ToJson();
            }

            using (Transaction t = new Transaction(doc, "CLI Create Family Instance"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                if (!symbol.IsActive)
                    symbol.Activate();

                var location = new XYZ(x.Value.MillimeterToFeet(), y.Value.MillimeterToFeet(), (z ?? 0).MillimeterToFeet());
                var instance = doc.Create.NewFamilyInstance(location, symbol, level, structuralType);

                t.Commit();

                var result = new
                {
                    element_id = instance?.Id.IntegerValue,
                    symbol_id = symbolId.Value,
                    level_id = levelId.Value
                };

                return CommandResponse.Success(cmd.TaskId, result, "Family instance created successfully.").ToJson();
            }
        }
    }
}
