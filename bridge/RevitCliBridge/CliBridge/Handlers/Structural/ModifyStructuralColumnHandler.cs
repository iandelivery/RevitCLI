using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Structural
{
    /// <summary>
    /// Modifies an existing structural column: base location point and/or
    /// family type. At least one optional field must be provided.
    /// Coordinates are in millimeters; omitted components keep their current
    /// value.
    /// </summary>
    public class ModifyStructuralColumnHandler : DocumentCommandBase
    {
        public override string CommandName => "modify_structural_column";
        public override string Description => "Modifies an existing structural column (location point, family type)";
        public override string Category => "Modify";
        public override string[] Aliases => new[] { "structural_column_modify" };
        public override bool SupportsDryRun => true;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Structural column element ID to modify" },
            new CommandParamSchema { Name = "x", Type = "double", Required = false, Description = "New base point X in millimeters" },
            new CommandParamSchema { Name = "y", Type = "double", Required = false, Description = "New base point Y in millimeters" },
            new CommandParamSchema { Name = "z", Type = "double", Required = false, Description = "New base point Z in millimeters" },
            new CommandParamSchema { Name = "symbol_id", Type = "int", Required = false, Description = "New column FamilySymbol ID (list via get_family_symbols --category OST_StructuralColumns)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"modify_structural_column\", \"parameters\": { \"element_id\": 12345, \"x\": 6000 } }",
            "{ \"command\": \"modify_structural_column\", \"parameters\": { \"element_id\": 12345, \"symbol_id\": 3413 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<ModifyStructuralColumnParams>(cmd, out var error);
            if (p is null) return error!;

            if (!HasAnyChange(p))
                return CommandResponse.Error(cmd.TaskId, "At least one of x, y, z, or symbol_id must be provided.").ToJson();

            var column = doc.GetElement(new ElementId(p.ElementId)) as FamilyInstance;
            if (column is null || !StructuralUtils.Default.IsColumn(column))
                return CommandResponse.Error(cmd.TaskId, $"Element {p.ElementId} is not a structural column.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Modify Structural Column", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();

                // Type change
                if (p.SymbolId.HasValue && p.SymbolId.Value > 0 && p.SymbolId.Value != column.GetTypeId().IntegerValue)
                {
                    var newSymbol = StructuralUtils.Default.ResolveSymbol(doc, p.SymbolId, BuiltInCategory.OST_StructuralColumns);
                    if (newSymbol is null)
                        return CommandResponse.Error(cmd.TaskId,
                            $"FamilySymbol with ID {p.SymbolId.Value} not found or is not a structural column type.").ToJson();
                    column.ChangeTypeId(newSymbol.Id);
                }

                // Location change — omitted components keep the current value.
                if (p.X.HasValue || p.Y.HasValue || p.Z.HasValue)
                {
                    var locPoint = column.Location as LocationPoint;
                    if (locPoint is null)
                        return CommandResponse.Error(cmd.TaskId, "Column does not have a point location.").ToJson();

                    var old = locPoint.Point;
                    locPoint.Point = new XYZ(
                        (p.X ?? old.X.FeetToMillimeter()).MillimeterToFeet(),
                        (p.Y ?? old.Y.FeetToMillimeter()).MillimeterToFeet(),
                        (p.Z ?? old.Z.FeetToMillimeter()).MillimeterToFeet());
                }

                tx.Commit();

                var result = StructuralUtils.Default.SnapshotColumn(column, doc);
                return CommandResponse.Success(cmd.TaskId, result, "Structural column modified successfully.").ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the modification: {ex.Message}").ToJson();
            }
        }

        private static bool HasAnyChange(ModifyStructuralColumnParams p) =>
            p.X.HasValue || p.Y.HasValue || p.Z.HasValue ||
            (p.SymbolId.HasValue && p.SymbolId.Value > 0);
    }

    /// <summary>
    /// Typed parameter bag for <see cref="ModifyStructuralColumnHandler"/>.
    /// </summary>
    public class ModifyStructuralColumnParams
    {
        [Param("element_id", Required = true)]
        public int ElementId { get; set; }

        [Param("x")]
        public double? X { get; set; }

        [Param("y")]
        public double? Y { get; set; }

        [Param("z")]
        public double? Z { get; set; }

        [Param("symbol_id")]
        public int? SymbolId { get; set; }
    }
}
