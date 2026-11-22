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
        /// Read the size of a duct in Revit internal units (feet). For round
        /// ducts, only <paramref name="diameter"/> is populated; for
        /// rectangular/oval ducts, only <paramref name="width"/> and
        /// <paramref name="height"/>.
        /// </summary>
        public (double diameter, double width, double height) GetSizeInternal(Duct duct)
        {
            var shape = duct.DuctType?.Shape ?? ConnectorProfileType.Round;

            if (shape == ConnectorProfileType.Round)
            {
                double diameter = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.AsDouble() ?? 0.0;
                return (diameter, 0.0, 0.0);
            }

            double width = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0.0;
            double height = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0.0;
            return (0.0, width, height);
        }

        /// <summary>
        /// Project a duct into a serializable snapshot with length fields in
        /// Revit internal units. Includes shape-specific size fields.
        /// </summary>
        public object Snapshot(Duct duct, Document doc)
        {
            var curve = (duct.Location as LocationCurve)?.Curve;
            var shape = duct.DuctType?.Shape ?? ConnectorProfileType.Round;

            double diameter = 0.0, width = 0.0, height = 0.0;
            if (shape == ConnectorProfileType.Round)
                diameter = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.AsDouble() ?? 0.0;
            else
            {
                width = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0.0;
                height = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0.0;
            }

            var typeName = duct.DuctType?.Name ?? "";
            var systemType = doc.GetElement(duct.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)?.AsElementId()) as MechanicalSystemType;

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
                diameter,
                width,
                height,
                length = curve?.Length ?? 0.0
            };
        }

        Element? IMepTypeResolver.ResolveType(Document doc, int? typeId) => ResolveType(doc, typeId);

        object IMepSnapshotter.Snapshot(MEPCurve element, Document doc) => Snapshot((Duct)element, doc);
    }
}