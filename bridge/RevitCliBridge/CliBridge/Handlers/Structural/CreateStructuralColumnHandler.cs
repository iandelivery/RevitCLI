using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Structural
{
    /// <summary>
    /// Creates a vertical structural column at a point on a level. The family
    /// type is optional — when omitted, the first loaded structural column
    /// type is used. Coordinates use the request length unit (default mm).
    /// </summary>
    public class CreateStructuralColumnHandler : DocumentCommandBase
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

        public override string CommandName => "create_structural_column";
        public override string Description => "Creates a structural column at a point on a level (type optional, falls back to the first loaded structural column type)";
        public override string Category => "Create";
        public override string[] Aliases => new[] { "structural_column_create" };
        public override bool SupportsDryRun => true;
        public override IReadOnlyDictionary<string, string> ResponseFieldKinds => FieldKinds;

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema { Name = "level_id", Type = "int", Required = true, Description = "Base level element ID to place the column on" },
            new CommandParamSchema { Name = "x", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "Base point X (request length unit, default mm)" },
            new CommandParamSchema { Name = "y", Type = "double", Required = true, UnitKind = UnitKinds.Length, Description = "Base point Y (request length unit, default mm)" },
            new CommandParamSchema { Name = "z", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Base point Z offset (request length unit, default mm)", Default = 0 },
            new CommandParamSchema { Name = "top_level_id", Type = "int", Required = false, Description = "Top constraint level element ID; omitted keeps the family default" },
            new CommandParamSchema { Name = "base_offset", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Base offset from the base level (request length unit, default mm)", Default = 0 },
            new CommandParamSchema { Name = "top_offset", Type = "double", Required = false, UnitKind = UnitKinds.Length, Description = "Top offset from the top level (request length unit, default mm)", Default = 0 },
            new CommandParamSchema { Name = "symbol_id", Type = "int", Required = false, Description = "FamilySymbol ID of the column type; defaults to the first loaded structural column type (list via get_family_symbols --category OST_StructuralColumns)" }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"create_structural_column\", \"parameters\": { \"level_id\": 3001, \"x\": 1000, \"y\": 2000 } }",
            "{ \"command\": \"create_structural_column\", \"parameters\": { \"level_id\": 3001, \"x\": 1000, \"y\": 2000, \"z\": 0, \"top_level_id\": 3002, \"base_offset\": 0, \"top_offset\": -100, \"symbol_id\": 3413 } }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var p = TryBind<CreateStructuralColumnParams>(cmd, out var error);
            if (p is null) return error!;

            var level = doc.GetElement(new ElementId(p.LevelId)) as Level;
            if (level is null)
                return CommandResponse.Error(cmd.TaskId, $"Level with ID {p.LevelId} not found.").ToJson();

            if (p.TopLevelId.HasValue && p.TopLevelId.Value > 0 &&
                doc.GetElement(new ElementId(p.TopLevelId.Value)) as Level is null)
                return CommandResponse.Error(cmd.TaskId, $"Top level with ID {p.TopLevelId.Value} not found.").ToJson();

            var symbol = StructuralUtils.Default.ResolveSymbol(doc, p.SymbolId, BuiltInCategory.OST_StructuralColumns);
            if (symbol is null)
            {
                if (p.SymbolId.HasValue && p.SymbolId.Value > 0)
                    return CommandResponse.Error(cmd.TaskId,
                        $"FamilySymbol with ID {p.SymbolId.Value} not found or is not a structural column type.").ToJson();
                return CommandResponse.Error(cmd.TaskId,
                    "No structural column family types are loaded in the document.").ToJson();
            }

            using var tx = new DryRunTransaction(doc, "CLI Create Structural Column", cmd.DryRun);
            try
            {
                tx.ConfigureFailureHandling();

                if (!symbol.IsActive)
                    symbol.Activate();

                var location = new XYZ(p.X, p.Y, p.Z);
                var column = doc.Create.NewFamilyInstance(location, symbol, level, StructuralType.Column);

                if (column.Location is LocationPoint lp)
                    lp.Point = location;

                if (p.TopLevelId.HasValue && p.TopLevelId.Value > 0)
                {
                    var top = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                    if (top is null || !top.Set(new ElementId(p.TopLevelId.Value)))
                        return CommandResponse.Error(cmd.TaskId,
                            "The created column does not support the Top Level parameter.").ToJson();
                }

                if (p.BaseOffset.HasValue)
                {
                    var off = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                    if (off is null || !off.Set(p.BaseOffset.Value))
                        return CommandResponse.Error(cmd.TaskId,
                            "The created column does not support the Base Offset parameter.").ToJson();
                }

                if (p.TopOffset.HasValue)
                {
                    var off = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                    if (off is null || !off.Set(p.TopOffset.Value))
                        return CommandResponse.Error(cmd.TaskId,
                            "The created column does not support the Top Offset parameter.").ToJson();
                }

                var result = StructuralUtils.Default.SnapshotColumn(column, doc);

                tx.Commit();

                return CommandResponse.SuccessWithResolvedFailures(cmd.TaskId, result, "Structural column created successfully.", tx.ResolvedFailures).ToJson();
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return CommandResponse.Error(cmd.TaskId, $"Revit rejected the creation: {ex.Message}").ToJson();
            }
        }
    }

    /// <summary>
    /// Typed parameter bag for <see cref="CreateStructuralColumnHandler"/>.
    /// </summary>
    public class CreateStructuralColumnParams
    {
        [Param("level_id", Required = true)]
        public int LevelId { get; set; }

        [Param("x", Required = true)]
        public double X { get; set; }

        [Param("y", Required = true)]
        public double Y { get; set; }

        [Param("z", Default = 0.0)]
        public double Z { get; set; }

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
