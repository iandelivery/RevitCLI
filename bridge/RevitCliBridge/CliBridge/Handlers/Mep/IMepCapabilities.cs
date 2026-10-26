using Autodesk.Revit.DB;

namespace RevitCliBridge.Handlers.Mep
{
    /// <summary>
    /// Resolve the concrete MEP element type for a discipline. Part of the
    /// per-discipline thin facade; kept on a small interface (interface
    /// segregation, dependency inversion) so the shared pipeline can treat
    /// every discipline uniformly and a new discipline (e.g. Conduit) can be
    /// added without touching existing facades or handlers (open/closed).
    /// </summary>
    internal interface IMepTypeResolver
    {
        /// <summary>
        /// Resolve the MEP type by element id, falling back to the first
        /// available type when <paramref name="typeId"/> is null, zero, or
        /// negative. Returns the type as an <see cref="Element"/> so the
        /// interface stays discipline-agnostic.
        /// </summary>
        Element? ResolveType(Document doc, int? typeId);
    }

    /// <summary>
    /// Project an MEP element into a serializable snapshot (coordinates in
    /// millimeters). Kept on a small interface for the same reasons as
    /// <see cref="IMepTypeResolver"/>.
    /// </summary>
    internal interface IMepSnapshotter
    {
        object Snapshot(MEPCurve element, Document doc);
    }
}