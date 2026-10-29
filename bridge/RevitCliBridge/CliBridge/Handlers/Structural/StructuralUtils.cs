using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitCliBridge.Handlers.Structural
{
    /// <summary>
    /// Thin facade for the Structural domain: family symbol resolution,
    /// typed column/beam checks, and snapshot projection. Modeled on
    /// <see cref="Handlers.Piping.PipeUtils"/> — a stateless singleton so
    /// the column and beam handlers stay structurally parallel to the other
    /// disciplines. Coordinates cross the API boundary in millimeters only.
    /// </summary>
    internal sealed class StructuralUtils
    {
        public static StructuralUtils Default { get; } = new();
        private StructuralUtils() { }

        /// <summary>
        /// Minimum curve length Revit accepts (~2.5 mm in feet). Mirrors
        /// <c>MepCurveGeometry.MinSegmentLengthFeet</c> without coupling the
        /// structural domain to the MEP geometry helper.
        /// </summary>
        public const double MinSegmentLengthFeet = (1.0 / 12.0) / 10.0;

        /// <summary>
        /// Resolve a family symbol by element id, falling back to the first
        /// available symbol in <paramref name="category"/> when symbolId is
        /// null, zero, or negative. When an explicit id is given it must
        /// resolve to a symbol of that category, otherwise null is returned
        /// (no silent substitution). Also returns null when the document has
        /// no symbol loaded for the category.
        /// </summary>
        public FamilySymbol? ResolveSymbol(Document doc, int? symbolId, BuiltInCategory category)
        {
            if (symbolId.HasValue && symbolId.Value > 0)
            {
                var byId = doc.GetElement(new ElementId(symbolId.Value)) as FamilySymbol;
                if (byId is null || !MatchesCategory(byId, category))
                    return null;
                return byId;
            }

            return new FilteredElementCollector(doc)
                .OfCategory(category)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();
        }

        /// <summary>
        /// True when the instance belongs to the structural columns category.
        /// </summary>
        public bool IsColumn(FamilyInstance instance) =>
            MatchesCategory(instance, BuiltInCategory.OST_StructuralColumns);

        /// <summary>
        /// True when the instance is a beam (StructuralType.Beam). Braces and
        /// other framing members also live in OST_StructuralFraming, so the
        /// structural usage must be checked, not just the category.
        /// </summary>
        public bool IsBeam(FamilyInstance instance) =>
            instance.StructuralType == StructuralType.Beam;

        /// <summary>
        /// Project a structural column into a serializable snapshot with the
        /// location point converted to millimeters.
        /// </summary>
        public object SnapshotColumn(FamilyInstance column, Document doc)
        {
            var symbol = doc.GetElement(column.GetTypeId()) as FamilySymbol;
            var point = (column.Location as LocationPoint)?.Point;

            return new
            {
                element_id = column.Id.IntegerValue,
                name = column.Name,
                type_id = column.GetTypeId().IntegerValue,
                type_name = symbol?.Name ?? "",
                family_name = symbol?.FamilyName ?? "",
                level_id = column.LevelId.IntegerValue,
                location = point is null ? null : new
                {
                    x = point.X.FeetToMillimeter(),
                    y = point.Y.FeetToMillimeter(),
                    z = point.Z.FeetToMillimeter()
                }
            };
        }

        /// <summary>
        /// Project a beam into a serializable snapshot with both endpoints
        /// and the length converted to millimeters.
        /// </summary>
        public object SnapshotBeam(FamilyInstance beam, Document doc)
        {
            var symbol = doc.GetElement(beam.GetTypeId()) as FamilySymbol;
            var curve = (beam.Location as LocationCurve)?.Curve;

            object? start = null, end = null;
            if (curve is not null)
            {
                var p1 = curve.GetEndPoint(0);
                var p2 = curve.GetEndPoint(1);
                start = new { x = p1.X.FeetToMillimeter(), y = p1.Y.FeetToMillimeter(), z = p1.Z.FeetToMillimeter() };
                end = new { x = p2.X.FeetToMillimeter(), y = p2.Y.FeetToMillimeter(), z = p2.Z.FeetToMillimeter() };
            }

            return new
            {
                element_id = beam.Id.IntegerValue,
                name = beam.Name,
                type_id = beam.GetTypeId().IntegerValue,
                type_name = symbol?.Name ?? "",
                family_name = symbol?.FamilyName ?? "",
                level_id = beam.LevelId.IntegerValue,
                start,
                end,
                length_mm = curve?.Length.FeetToMillimeter() ?? 0.0
            };
        }

        private static bool MatchesCategory(Element element, BuiltInCategory category) =>
            element.Category is not null &&
            (BuiltInCategory)element.Category.Id.IntegerValue == category;
    }
}
