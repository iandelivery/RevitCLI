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
                return doc.GetElement(ElementIdCompat.FromValue(typeId.Value)) as CableTrayType;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(CableTrayType))
                .FirstOrDefault() as CableTrayType;
        }

        /// <summary>
        /// Read the width and height of a cable tray in Revit internal units
        /// (feet). Reads from the instance parameters
        /// (RBS_CABLETRAY_WIDTH_PARAM / RBS_CABLETRAY_HEIGHT_PARAM), which
        /// reflect either the type default or any instance override.
        /// </summary>
        public (double width, double height) GetSizeInternal(CableTray ct)
        {
            double width = ct.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? 0.0;
            double height = ct.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? 0.0;
            return (width, height);
        }

        /// <summary>
        /// Project a cable tray into a serializable snapshot with length
        /// fields in Revit internal units.
        /// </summary>
        public object Snapshot(CableTray ct, Document doc)
        {
            var curve = (ct.Location as LocationCurve)?.Curve;
            double width = ct.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? 0.0;
            double height = ct.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? 0.0;
            var typeName = (doc.GetElement(ct.GetTypeId()) as CableTrayType)?.Name ?? "";

            object? start = null, end = null;
            if (curve is not null)
            {
                var p1 = curve.GetEndPoint(0);
                var p2 = curve.GetEndPoint(1);
                start = new { x = p1.X, y = p1.Y, z = p1.Z };
                end = new { x = p2.X, y = p2.Y, z = p2.Z };
            }

            return new
            {
                element_id = ElementIdCompat.ValueOf(ct.Id),
                name = ct.Name,
                type_id = ElementIdCompat.ValueOf(ct.GetTypeId()),
                type_name = typeName,
                level_id = ElementIdCompat.ValueOf(ct.LevelId),
                start,
                end,
                width,
                height,
                length = curve?.Length ?? 0.0
            };
        }

        Element? IMepTypeResolver.ResolveType(Document doc, int? typeId) => ResolveType(doc, typeId);

        object IMepSnapshotter.Snapshot(MEPCurve element, Document doc) => Snapshot((CableTray)element, doc);
    }
}