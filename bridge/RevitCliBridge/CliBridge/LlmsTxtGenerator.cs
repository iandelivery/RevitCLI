using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RevitCliBridge
{
    /// <summary>
    /// Generates an llms.txt reference file that describes the raw Revit API
    /// elements, parameters, and classes available in the running instance.
    /// This enables AI agents to discover uncovered API structures and pipe
    /// them into the raw/execute_raw fallback commands.
    /// </summary>
    public static class LlmsTxtGenerator
    {
        /// <summary>
        /// Generate the llms.txt content for the current Revit instance.
        /// Combines static API reference data with runtime-enriched instance data.
        /// </summary>
        public static string Generate(UIApplication uiApp, int revitVersion, int port, int pid)
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("# Revit CLI Bridge - API Reference");
            sb.AppendLine($"# Instance: Revit {revitVersion} | Port: {port} | PID: {pid}");
            sb.AppendLine();

            // 1. Built-in categories
            sb.AppendLine("## BuiltIn Categories");
            sb.AppendLine("# Format: BIC_NAME -> CategoryType");
            try
            {
                var categories = uiApp.ActiveUIDocument?.Document?.Settings?.Categories;
                if (categories != null)
                {
                    foreach (Category cat in categories)
                    {
                        sb.AppendLine($"{cat.Name} -> {cat.CategoryType}");
                    }
                }
                else
                {
                    sb.AppendLine("# (No active document — listing static categories)");
                    AppendStaticCategories(sb);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"# Error enumerating categories: {ex.Message}");
                AppendStaticCategories(sb);
            }
            sb.AppendLine();

            // 2. Common BuiltIn Parameters
            sb.AppendLine("## Common BuiltIn Parameters");
            sb.AppendLine("# Format: PARAMETER_NAME -> DataType (Group)");
            AppendCommonParameters(sb);
            sb.AppendLine();

            // 3. Element class hierarchy (static reference)
            sb.AppendLine("## Element Hierarchy");
            sb.AppendLine("# Key classes for use with execute_raw");
            AppendElementHierarchy(sb);
            sb.AppendLine();

            // 4. Filter classes (static reference)
            sb.AppendLine("## Element Filters");
            sb.AppendLine("# For use with filtered element collectors");
            AppendFilterClasses(sb);
            sb.AppendLine();

            // 5. Dynamic: loaded families (requires active document)
            sb.AppendLine("## Dynamic: Loaded Families");
            try
            {
                var doc = uiApp.ActiveUIDocument?.Document;
                if (doc != null)
                {
                    var collector = new FilteredElementCollector(doc)
                        .OfClass(typeof(Family));

                    var familyGroups = collector
                        .Cast<Family>()
                        .GroupBy(f => f.FamilyCategory?.Name ?? "Unknown")
                        .OrderBy(g => g.Key);

                    foreach (var group in familyGroups)
                    {
                        sb.AppendLine($"[{group.Key}]");
                        foreach (var family in group.Take(20)) // Limit per category
                        {
                            sb.AppendLine($"  {family.Name} ({family.GetFamilySymbolIds().Count} types)");
                        }
                        if (group.Count() > 20)
                            sb.AppendLine($"  ... and {group.Count() - 20} more");
                    }
                }
                else
                {
                    sb.AppendLine("# (No active document)");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"# Error enumerating families: {ex.Message}");
            }
            sb.AppendLine();

            // 6. Dynamic: project parameters (requires active document)
            sb.AppendLine("## Dynamic: Project Parameters");
            try
            {
                var doc = uiApp.ActiveUIDocument?.Document;
                if (doc != null)
                {
                    // Shared parameters via BindingMap
                    var bindingMap = doc.ParameterBindings;
                    var bindingIter = bindingMap.ForwardIterator();
                    while (bindingIter.MoveNext())
                    {
                        var definition = bindingIter.Key;
                        var binding = bindingIter.Current;
                        var bindingType = binding is InstanceBinding ? "Instance" : "Type";
                        string categories = "";
                        if (binding is InstanceBinding instanceBinding)
                        {
                            categories = string.Join(", ", instanceBinding.Categories.Cast<Category>().Select(c => c.Name));
                        }
                        else if (binding is TypeBinding typeBinding)
                        {
                            categories = string.Join(", ", typeBinding.Categories.Cast<Category>().Select(c => c.Name));
                        }
                        sb.AppendLine($"{definition.Name} -> {bindingType} ({categories})");
                    }
                }
                else
                {
                    sb.AppendLine("# (No active document)");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"# Error enumerating project parameters: {ex.Message}");
            }
            sb.AppendLine();

            // 7. Available commands
            sb.AppendLine("## Registered Commands");
            sb.AppendLine("# Commands available via the bridge");
            foreach (var handler in CommandRouter.GetAllHandlers())
            {
                sb.AppendLine($"  {handler.CommandName} — {handler.Description}");
            }
            sb.AppendLine();

            // 8. Usage hint
            sb.AppendLine("## Usage");
            sb.AppendLine("Use the `raw` or `execute_raw` commands to invoke uncovered Revit API elements:");
            sb.AppendLine("  revit-cli.exe raw -j '{\"command\":\"execute_raw\",\"parameters\":{\"code\":\"return doc.Title;\",\"lang\":\"csharp\"}}'");
            sb.AppendLine("  revit-cli.exe execute_raw --lang csharp --code \"return doc.Title;\"");

            return sb.ToString();
        }

        /// <summary>
        /// Append static category names (used when no document is open).
        /// </summary>
        private static void AppendStaticCategories(StringBuilder sb)
        {
            var staticCategories = new[]
            {
                "OST_Walls", "OST_Doors", "OST_Windows", "OST_Floors",
                "OST_Roofs", "OST_Ceilings", "OST_Stairs", "OST_Ramps",
                "OST_Columns", "OST_StructuralColumns", "OST_StructuralFraming",
                "OST_StructuralFoundation", "OST_Pipes", "OST_Ducts",
                "OST_Conduit", "OST_CableTray", "OST_Furniture",
                "OST_PlumbingFixtures", "OST_SpecialityEquipment",
                "OST_CurtaSystem", "OST_CurtainWallPanels",
                "OST_CurtainWallMullions", "OST_DetailComponents",
                "OST_Lines", "OST_Text", "OST_Dimensions",
                "OST_Tags", "OST_Views", "OST_Sheets",
                "OST_Levels", "OST_Grids", "OST_ReferencePlanes",
                "OST_Site", "OST_Topography", "OST_Parking",
                "OST_Railing", "OST_StairsRailing"
            };
            foreach (var cat in staticCategories)
            {
                sb.AppendLine(cat);
            }
        }

        /// <summary>
        /// Append common BuiltIn parameter names for reference.
        /// </summary>
        private static void AppendCommonParameters(StringBuilder sb)
        {
            var commonParams = new[]
            {
                "DOOR_NUMBER -> Text (Identity)",
                "DOOR_WIDTH -> Length (Dimensions)",
                "DOOR_HEIGHT -> Length (Dimensions)",
                "WALL_BASE_OFFSET -> Length (Constraints)",
                "WALL_TOP_OFFSET -> Length (Constraints)",
                "WALL_BASE_IS_ATTACHED -> YesNo (Constraints)",
                "WALL_TOP_IS_ATTACHED -> YesNo (Constraints)",
                "WALL_STRUCTURAL_SIGNIFICANT -> YesNo (Structural)",
                "WALL_USER_HEIGHT_PARAM -> Length (Dimensions)",
                "WINDOW_WIDTH -> Length (Dimensions)",
                "WINDOW_HEIGHT -> Length (Dimensions)",
                "WINDOW_SILL_HEIGHT -> Length (Dimensions)",
                "FLOOR_HEIGHTABOVELEVEL_PARAM -> Length (Constraints)",
                "FLOOR_THICKNESS_PARAM -> Length (Dimensions)",
                "ROOF_BASE_LEVEL_PARAM -> Length (Constraints)",
                "ROOF_SLOPE -> Number (Dimensions)",
                "CEILING_HEIGHTABOVELEVEL_PARAM -> Length (Constraints)",
                "STAIRS_BASE_LEVEL -> ElementId (Constraints)",
                "STAIRS_TOP_LEVEL -> ElementId (Constraints)",
                "STAIRS_ACTUAL_RISERS -> Integer (Dimensions)",
                "STAIRS_ACTUAL_TREADS -> Integer (Dimensions)",
                "COLUMN_BASE_LEVEL_PARAM -> ElementId (Constraints)",
                "COLUMN_TOP_LEVEL_PARAM -> ElementId (Constraints)",
                "COLUMN_BASE_OFFSET_PARAM -> Length (Constraints)",
                "COLUMN_TOP_OFFSET_PARAM -> Length (Constraints)",
                "ELEM_FAMILY_PARAM -> ElementId (Identity)",
                "ELEM_FAMILY_AND_TYPE_PARAM -> ElementId (Identity)",
                "ELEM_TYPE_PARAM -> ElementId (Identity)",
                "ALL_MODEL_MARK -> Text (Identity)",
                "ALL_MODEL_COMMENTS -> Text (Identity)",
                "ALL_MODEL_IMAGE -> Image (Identity)",
                "INSTANCE_MOVE_BASE_POINT -> Integer (Other)",
                "INSTANCE_MIRROR_STATE -> YesNo (Other)",
            };
            foreach (var p in commonParams)
            {
                sb.AppendLine(p);
            }
        }

        /// <summary>
        /// Append the key Revit API element class hierarchy.
        /// </summary>
        private static void AppendElementHierarchy(StringBuilder sb)
        {
            var hierarchy = new[]
            {
                "Element",
                "  -> CurveElement",
                "    -> DetailCurve",
                "    -> ModelCurve",
                "    -> SymbolicCurve",
                "  -> FamilyInstance",
                "  -> HostObject",
                "    -> Ceiling",
                "    -> Floor",
                "    -> RoofBase",
                "      -> FootPrintRoof",
                "      -> ExtrusionRoof",
                "    -> Wall",
                "  -> Level",
                "  -> Grid",
                "  -> View",
                "    -> View3D",
                "    -> ViewPlan",
                "    -> ViewSection",
                "    -> ViewSheet",
                "    -> ViewSchedule",
                "  -> Family",
                "  -> ElementType",
                "    -> FamilySymbol",
                "      -> FamilyType",
                "  -> ParameterElement",
                "    -> SharedParameterElement",
                "  -> DatumPlane",
                "    -> ReferencePlane",
                "  -> Sketch",
                "  -> Revision",
                "  -> Sheet",
            };
            foreach (var line in hierarchy)
            {
                sb.AppendLine(line);
            }
        }

        /// <summary>
        /// Append element filter classes for use with FilteredElementCollector.
        /// </summary>
        private static void AppendFilterClasses(StringBuilder sb)
        {
            var filters = new[]
            {
                "ElementCategoryFilter(BuiltInCategory)",
                "ElementClassFilter(Type)",
                "ElementIsElementTypeFilter()",
                "ElementOwnerViewFilter()",
                "ElementStructuralTypeFilter(StructuralType)",
                "ElementWorksetFilter(WorksetId)",
                "ExclusionFilter(ICollection<ElementId>)",
                "FamilyInstanceFilter(Family)",
                "LogicalAndFilter(IList<ElementFilter>)",
                "LogicalOrFilter(IList<ElementFilter>)",
                "BoundingBoxIntersectsFilter(Outline)",
                "BoundingBoxIsInsideFilter(Outline)",
                "ElementIntersectsSolidFilter(Solid)",
                "ElementIntersectsElementFilter(Element)",
            };
            foreach (var f in filters)
            {
                sb.AppendLine(f);
            }
        }
    }
}
