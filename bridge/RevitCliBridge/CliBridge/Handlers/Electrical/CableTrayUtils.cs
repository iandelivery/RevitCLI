using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers.Mep;

namespace RevitCliBridge.Handlers.Electrical
{
    /// <summary>
    /// Thin per-discipline facade for CableTray: type resolution, size
    /// reading, and snapshot projection. All geometry/connector/fitting math
    /// lives in the shared <see cref="MepCurveGeometry"/> and
    /// <see cref="MepFittingResolver"/> — this class only knows what is
    /// CableTray-specific. Implements the shared capability interfaces (SOLID)
    /// for uniform, open/closed extension across all MEP disciplines.
    /// </summary>
    internal sealed class CableTrayUtils : IMepTypeResolver, IMepSnapshotter
    {
        public static CableTrayUtils Default { get; } = new();
        private CableTrayUtils() { }

        /// <summary>
        /// Resolve a cable tray type by element id, falling back to the first
        /// available type in the document when <paramref name="typeId"/> is
        /// null, zero, or negative.
        /// </summary>
        public CableTrayType? ResolveType(Document doc, int? typeId)
        {
            if (typeId.HasValue && typeId.Value > 0)
                return doc.GetElement(new ElementId(typeId.Value)) as CableTrayType;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(CableTrayType))
                .FirstOrDefault() as CableTrayType;
        }

        /// <summary>
        /// Read the width and height of a cable tray in millimeters. Reads
        /// from the instance parameters (RBS_CABLETRAY_WIDTH_PARAM /
        /// RBS_CABLETRAY_HEIGHT_PARAM), which reflect either the type default
        /// or any instance override.
        /// </summary>
        public (double widthMm, double heightMm) GetSize(CableTray ct)
        {
            double widthFt = ct.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? 0.0;
            double heightFt = ct.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? 0.0;
            return (widthFt.FeetToMillimeter(), heightFt.FeetToMillimeter());
        }

        /// <summary>
        /// Project a cable tray into a serializable snapshot with all
        /// coordinates converted to millimeters.
        /// </summary>
        public object Snapshot(CableTray ct, Document doc)
        {
            var curve = (ct.Location as LocationCurve)?.Curve;
            var (widthMm, heightMm) = GetSize(ct);
            var typeName = (doc.GetElement(ct.GetTypeId()) as CableTrayType)?.Name ?? "";

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
                element_id = ct.Id.IntegerValue,
                name = ct.Name,
                type_id = ct.GetTypeId().IntegerValue,
                type_name = typeName,
                level_id = ct.LevelId.IntegerValue,
                start,
                end,
                width_mm = widthMm,
                height_mm = heightMm,
                length_mm = curve?.Length.FeetToMillimeter() ?? 0.0
            };
        }

        // Explicit interface implementations route the discipline-agnostic
        // contract (MEPCurve / Element) onto the CableTray-typed surface above.
        Element? IMepTypeResolver.ResolveType(Document doc, int? typeId) => ResolveType(doc, typeId);

        object IMepSnapshotter.Snapshot(MEPCurve element, Document doc) => Snapshot((CableTray)element, doc);
    }
}