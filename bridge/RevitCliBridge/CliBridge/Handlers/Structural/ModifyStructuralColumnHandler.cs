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
    /// Coordinates use the request length unit (default mm); omitted
    /// components keep their current value.
    /// </summary>
    public class ModifyStructuralColumnHandler : DocumentCommandBase
    {
        private static readonly IReadOnlyDictionary<string, string> FieldKinds =
            new Dictionary<string, string>
            {
                ["x"] = UnitKinds.Length,
                ["y"] = UnitKinds.Length,
                ["z"] = UnitKinds.Length,
                ["base_offset"] = UnitKinds.Length,
                ["top_offset"] = UnitKinds.Length
            };

        public override string CommandName => "modify_structural_column";
        public override string Description => "Modifies an existing structural column (location point, family type)";
        public override string Category => "Modify";
        public override string[] Aliases => new[] { "structural_column_modify" };
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "element_id", Type = "int", Required = true, Description = "Structural column element ID to modify" },
            new CommandParamSchema { Name = "x", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New base point X (request length unit, default mm)" },
            new CommandParamSchema { Name = "y", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New base point Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "z", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New base point Z (request length unit, default mm)" },
            new CommandParamSchema { Name = "base_level_id", Type = "int", Required = false, Description = "New base level element ID" },
            new CommandParamSchema { Name = "top_level_id", Type = "int", Required = false, Description = "New top constraint level element ID" },
            new CommandParamSchema { Name = "base_offset", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New base offset from the base level (request length unit, default mm)" },
            new CommandParamSchema { Name = "top_offset", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "New top offset from the top level (request length unit, default mm)" },
            new CommandParamSchema { Name = "symbol_id", Type = "int", Required = false, Description = "New column FamilySymbol ID (list via get_family_symbols --category OST_StructuralColumns)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"modify_structural_column\", \"parameters\": { \"element_id\": 12345, \"x\": 6000 } }",
            "{ \"command\": \"modify_structural_column\", \"parameters\": { \"element_id\": 12345, \"base_level_id\": 3001, \"top_level_id\": 3002, \"base_offset\": 0, \"top_offset\": -100 } }",
            "{ \"command\": \"modify_structural_column\", \"parameters\": { \"element_id\": 12345, \"symbol_id\": 3413 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<ModifyStructuralColumnParams>(cmd, out var error);
            if (p is null) return error!;

            if (!HasAnyChange(p))
                return CommandResponse.Error(cmd.TaskId,
                    "At least one of x, y, z, base_level_id, top_level_id, base_offset, top_offset, or symbol_id must be provided.").ToJson();

            var column = doc.GetElement(ElementIdCompat.FromValue(p.ElementId)) as FamilyInstance;
            if (column is null || !StructuralUtils.Default.IsColumn(column))
                return CommandResponse.Error(cmd.TaskId, $"Element {p.ElementId} is not a structural column.").ToJson();

            if (p.BaseLevelId.HasValue && p.BaseLevelId.Value > 0 &&
                doc.GetElement(ElementIdCompat.FromValue(p.BaseLevelId.Value)) as Level is null)
                return CommandResponse.Error(cmd.TaskId, $"Base level with ID {p.BaseLevelId.Value} not found.").ToJson();

            if (p.TopLevelId.HasValue && p.TopLevelId.Value > 0 &&
                doc.GetElement(ElementIdCompat.FromValue(p.TopLevelId.Value)) as Level is null)
                return CommandResponse.Error(cmd.TaskId, $"Top level with ID {p.TopLevelId.Value} not found.").ToJson();

            using var tx = new DryRunTransaction(doc, "CLI Modify Structural Column", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();

                if (p.SymbolId.HasValue && p.SymbolId.Value > 0 && p.SymbolId.Value != ElementIdCompat.ValueOf(column.GetTypeId()))
                {
                    var newSymbol = StructuralUtils.Default.ResolveSymbol(doc, p.SymbolId, BuiltInCategory.OST_StructuralColumns);
                    if (newSymbol is null)
                        return CommandResponse.Error(cmd.TaskId,
                            $"FamilySymbol with ID {p.SymbolId.Value} not found or is not a structural column type.").ToJson();
                    column.ChangeTypeId(newSymbol.Id);
                }

                if (p.X.HasValue || p.Y.HasValue || p.Z.HasValue)
                {
                    var locPoint = column.Location as LocationPoint;
                    if (locPoint is null)
                        return CommandResponse.Error(cmd.TaskId, "Column does not have a point location.").ToJson();

                    var old = locPoint.Point;
                    locPoint.Point = new XYZ(
                        p.X ?? old.X,
                        p.Y ?? old.Y,
                        p.Z ?? old.Z);
                }

                if (p.BaseLevelId.HasValue && p.BaseLevelId.Value > 0)
                {
                    var prm = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
                    if (prm is null || !prm.Set(ElementIdCompat.FromValue(p.BaseLevelId.Value)))
                        return CommandResponse.Error(cmd.TaskId, "Column does not support the Base Level parameter.").ToJson();
                }

                if (p.TopLevelId.HasValue && p.TopLevelId.Value > 0)
                {
                    var prm = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                    if (prm is null || !prm.Set(ElementIdCompat.FromValue(p.TopLevelId.Value)))
                        return CommandResponse.Error(cmd.TaskId, "Column does not support the Top Level parameter.").ToJson();
                }

                if (p.BaseOffset.HasValue)
                {
                    var prm = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                    if (prm is null || !prm.Set(p.BaseOffset.Value))
                        return CommandResponse.Error(cmd.TaskId, "Column does not support the Base Offset parameter.").ToJson();
                }

                if (p.TopOffset.HasValue)
                {
                    var prm = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                    if (prm is null || !prm.Set(p.TopOffset.Value))
                        return CommandResponse.Error(cmd.TaskId, "Column does not support the Top Offset parameter.").ToJson();
                }

                tx.Commit();

                var result = StructuralUtils.Default.SnapshotColumn(column, doc);
                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Structural column modified successfully.", tx.ResolvedFailures).ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the modification: {ex.Message}").ToJson();
            }
        }

        private static bool HasAnyChange(ModifyStructuralColumnParams p) =>
            p.X.HasValue || p.Y.HasValue || p.Z.HasValue ||
            (p.BaseLevelId.HasValue && p.BaseLevelId.Value > 0) ||
            (p.TopLevelId.HasValue && p.TopLevelId.Value > 0) ||
            p.BaseOffset.HasValue || p.TopOffset.HasValue ||
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

        [Param("base_level_id")]
        public int? BaseLevelId { get; set; }

        [Param("top_level_id")]
        public int? TopLevelId { get; set; }

        [Param("base_offset")]
        public double? BaseOffset { get; set; }

        [Param("top_offset")]
        public double? TopOffset { get; set; }

        [Param("symbol_id")]
        public int? SymbolId { get; set; }
    }
}
