using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Creation
{
    public class CreateFamilyInstanceHandler : DocumentCommandBase
    {
        public override string CommandName => "create_family_instance";
        public override string Description => "Creates a family instance at a specified point on a level";
        public override string Category => "Create";
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "symbol_id", Type = "int", Required = true, Description = "FamilySymbol element ID to instantiate" },
            new CommandParamSchema { Name = "level_id", Type = "int", Required = true, Description = "Level element ID to place the instance on" },
            new CommandParamSchema { Name = "x", Type = "double", Required = true, Description = "Insertion point X in millimeters" },
            new CommandParamSchema { Name = "y", Type = "double", Required = true, Description = "Insertion point Y in millimeters" },
            new CommandParamSchema { Name = "z", Type = "double", Required = false, Description = "Insertion point Z in millimeters", Default = 0 },
            new CommandParamSchema { Name = "structural_type", Type = "string", Required = false, Description = "Structural type", EnumValues = new[] { "NonStructural", "Beam", "Column", "Brace", "Footing", "UnknownFraming", "UnknownStructural" }, Default = "NonStructural" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_family_instance\", \"parameters\": { \"symbol_id\": 12345, \"level_id\": 3001, \"x\": 1000, \"y\": 2000 } }",
            "{ \"command\": \"create_family_instance\", \"parameters\": { \"symbol_id\": 12345, \"level_id\": 3001, \"x\": 1000, \"y\": 2000, \"z\": 500, \"structural_type\": \"Column\" } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<CreateFamilyInstanceParams>(cmd, out var error);
            if (p is null) return error!;

            var symbol = doc.GetElement(new ElementId(p.SymbolId)) as FamilySymbol;
            if (symbol is null)
                return CommandResponse.Error(cmd.TaskId, $"FamilySymbol with ID {p.SymbolId} not found.").ToJson();

            var level = doc.GetElement(new ElementId(p.LevelId)) as Level;
            if (level is null)
                return CommandResponse.Error(cmd.TaskId, $"Level with ID {p.LevelId} not found.").ToJson();

            StructuralType structuralType = StructuralType.NonStructural;
            if (!string.IsNullOrEmpty(p.StructuralType))
            {
                if (!Enum.TryParse(p.StructuralType, out structuralType))
                    return CommandResponse.Error(cmd.TaskId,
                        $"Invalid StructuralType: {p.StructuralType}. Valid values: {string.Join(", ", Enum.GetNames(typeof(StructuralType)))}").ToJson();
            }

            using (Transaction t = new Transaction(doc, "CLI Create Family Instance"))
            {
                t.Start();
                t.ConfigureFailureHandling();

                if (!symbol.IsActive)
                    symbol.Activate();

                var location = new XYZ(p.X.MillimeterToFeet(), p.Y.MillimeterToFeet(), p.Z.MillimeterToFeet());
                var instance = doc.Create.NewFamilyInstance(location, symbol, level, structuralType);

                t.Commit();

                var result = new
                {
                    element_id = instance?.Id.IntegerValue,
                    symbol_id = p.SymbolId,
                    level_id = p.LevelId
                };

                return CommandResponse.Success(cmd.TaskId, result, "Family instance created successfully.").ToJson();
            }
        }
    }

    /// <summary>
    /// Typed parameter bag for <see cref="CreateFamilyInstanceHandler"/>.
    /// </summary>
    public class CreateFamilyInstanceParams
    {
        [Param("symbol_id", Required = true)]
        public int SymbolId { get; set; }

        [Param("level_id", Required = true)]
        public int LevelId { get; set; }

        [Param("x", Required = true)]
        public double X { get; set; }

        [Param("y", Required = true)]
        public double Y { get; set; }

        [Param("z", Default = 0.0)]
        public double Z { get; set; }

        [Param("structural_type", Default = "NonStructural")]
        public string? StructuralType { get; set; }
    }
}
