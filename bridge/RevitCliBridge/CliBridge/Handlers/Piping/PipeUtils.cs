using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Handlers.Mep;

namespace RevitCliBridge.Handlers.Piping
{
    /// <summary>
    /// Thin per-discipline facade for Pipe: type/system resolution, diameter
    /// reading, and snapshot projection. All geometry/connector math is
    /// delegated to the shared <see cref="MepCurveGeometry"/>; this class only
    /// knows what is Pipe-specific. Implements the shared capability
    /// interfaces (SOLID) so it can be swapped/additively extended like the
    /// Duct and CableTray facades. Pipe command handlers are added in a later
    /// milestone; this facade is created now to keep the three disciplines
    /// structurally parallel.
    /// </summary>
    internal sealed class PipeUtils : IMepTypeResolver, IMepSnapshotter
    {
        public static PipeUtils Default { get; } = new();
        private PipeUtils() { }

        /// <summary>
        /// Resolve a pipe type by element id, falling back to the first
        /// available type in the document when <paramref name="typeId"/> is
        /// null, zero, or negative.
        /// </summary>
        public PipeType? ResolveType(Document doc, int? typeId)
        {
            if (typeId.HasValue && typeId.Value > 0)
                return doc.GetElement(new ElementId(typeId.Value)) as PipeType;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .FirstOrDefault() as PipeType;
        }

        /// <summary>
        /// Resolve a piping (supply/drain/fire) system type by element id.
        /// Returns null when the id does not resolve to a PipingSystemType.
        /// </summary>
        public PipingSystemType? ResolveSystemType(Document doc, int? systemTypeId)
        {
            if (!systemTypeId.HasValue || systemTypeId.Value <= 0)
                return null;
            return doc.GetElement(new ElementId(systemTypeId.Value)) as PipingSystemType;
        }

        /// <summary>
        /// Read the diameter of a pipe in millimeters from its instance
        /// parameter (pipes are round; the size is stored on the pipe rather
        /// than its type the way duct size is).
        /// </summary>
        public double GetDiameterMm(Pipe pipe)
        {
            double diameterFt = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0.0;
            return diameterFt.FeetToMillimeter();
        }

        /// <summary>
        /// Project a pipe into a serializable snapshot with all coordinates
        /// converted to millimeters.
        /// </summary>
        public object Snapshot(Pipe pipe, Document doc)
        {
            var curve = (pipe.Location as LocationCurve)?.Curve;
            double diameterMm = GetDiameterMm(pipe);
            var typeName = (doc.GetElement(pipe.GetTypeId()) as PipeType)?.Name ?? "";
            var systemType = doc.GetElement(pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId()) as PipingSystemType;

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
                element_id = pipe.Id.IntegerValue,
                name = pipe.Name,
                type_id = pipe.GetTypeId().IntegerValue,
                type_name = typeName,
                system_type_id = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId().IntegerValue ?? 0,
                system_type_name = systemType?.Name ?? "",
                level_id = pipe.LevelId.IntegerValue,
                start,
                end,
                diameter_mm = diameterMm,
                length_mm = curve?.Length.FeetToMillimeter() ?? 0.0
            };
        }

        // Explicit interface implementations route the discipline-agnostic
        // contract (MEPCurve / Element) onto the Pipe-typed surface above.
        Element? IMepTypeResolver.ResolveType(Document doc, int? typeId) => ResolveType(doc, typeId);

        object IMepSnapshotter.Snapshot(MEPCurve element, Document doc) => Snapshot((Pipe)element, doc);
    }
}