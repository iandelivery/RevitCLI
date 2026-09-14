using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers.Mep;

namespace RevitCliBridge.Handlers.Mechanical
{
    /// <summary>
    /// Thin per-discipline facade for Duct: type/system resolution, size
    /// reading, and snapshot projection. All geometry/connector/fitting math
    /// lives in the shared <see cref="MepCurveGeometry"/> and
    /// <see cref="MepFittingResolver"/> — this class only knows what is
    /// Duct-specific. Implements the shared capability interfaces (SOLID) for
    /// uniform, open/closed extension across all MEP disciplines.
    /// </summary>
    internal sealed class DuctUtils : IMepTypeResolver, IMepSnapshotter
    {
        public static DuctUtils Default { get; } = new();
        private DuctUtils() { }

        /// <summary>
        /// Resolve a duct type by element id, falling back to the first
        /// available type in the document when <paramref name="typeId"/> is
        /// null, zero, or negative.
        /// </summary>
        public DuctType? ResolveType(Document doc, int? typeId)
        {
            if (typeId.HasValue && typeId.Value > 0)
                return doc.GetElement(new ElementId(typeId.Value)) as DuctType;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(DuctType))
                .FirstOrDefault() as DuctType;
        }

        /// <summary>
        /// Resolve a mechanical (HVAC) system type by element id. Returns
        /// null when the id does not resolve to a MechanicalSystemType.
        /// </summary>
        public MechanicalSystemType? ResolveSystemType(Document doc, int? systemTypeId)
        {
            if (!systemTypeId.HasValue || systemTypeId.Value <= 0)
                return null;
            return doc.GetElement(new ElementId(systemTypeId.Value)) as MechanicalSystemType;
        }

        /// <summary>
        /// Read the size of a duct in millimeters. For round ducts, only
        /// <paramref name="diameterMm"/> is populated; for rectangular/oval
        /// ducts, only <paramref name="widthMm"/> and <paramref name="heightMm"/>.
        /// </summary>
        public (double diameterMm, double widthMm, double heightMm) GetSize(Duct duct)
        {
            var shape = duct.DuctType?.Shape ?? ConnectorProfileType.Round;

            if (shape == ConnectorProfileType.Round)
            {
                double diaFt = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.AsDouble() ?? 0.0;
                return (diaFt.FeetToMillimeter(), 0.0, 0.0);
            }

            double widthFt = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0.0;
            double heightFt = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0.0;
            return (0.0, widthFt.FeetToMillimeter(), heightFt.FeetToMillimeter());
        }

        /// <summary>
        /// Project a duct into a serializable snapshot with all coordinates
        /// converted to millimeters. Includes shape-specific size fields.
        /// </summary>
        public object Snapshot(Duct duct, Document doc)
        {
            var curve = (duct.Location as LocationCurve)?.Curve;
            var shape = duct.DuctType?.Shape ?? ConnectorProfileType.Round;
            var (diameterMm, widthMm, heightMm) = GetSize(duct);
            var typeName = duct.DuctType?.Name ?? "";
            var systemType = doc.GetElement(duct.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)?.AsElementId()) as MechanicalSystemType;

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
                element_id = duct.Id.IntegerValue,
                name = duct.Name,
                type_id = duct.DuctType?.Id.IntegerValue ?? 0,
                type_name = typeName,
                shape = shape.ToString().ToLowerInvariant(),
                system_type_id = duct.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)?.AsElementId().IntegerValue ?? 0,
                system_type_name = systemType?.Name ?? "",
                level_id = duct.LevelId.IntegerValue,
                start,
                end,
                diameter_mm = diameterMm,
                width_mm = widthMm,
                height_mm = heightMm,
                length_mm = curve?.Length.FeetToMillimeter() ?? 0.0
            };
        }

        // Explicit interface implementations route the discipline-agnostic
        // contract (MEPCurve / Element) onto the Duct-typed surface above.
        Element? IMepTypeResolver.ResolveType(Document doc, int? typeId) => ResolveType(doc, typeId);

        object IMepSnapshotter.Snapshot(MEPCurve element, Document doc) => Snapshot((Duct)element, doc);
    }
}