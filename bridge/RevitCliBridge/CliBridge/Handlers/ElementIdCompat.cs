using Autodesk.Revit.DB;

namespace RevitCliBridge
{
    /// <summary>
    /// ElementId construction and value access that compiles on every
    /// supported Revit version. Revit 2024 deprecates the ElementId(int)
    /// constructor and the IntegerValue property as errors, and only 2024
    /// offers the ElementId(long) constructor and the Value property. All
    /// other code calls this facade, so the version difference stays in one
    /// class. Pure static facade with no state, per the repo convention of
    /// facades over base classes.
    /// </summary>
    internal static class ElementIdCompat
    {
        /// <summary>
        /// An ElementId for the given value, from a client id or another
        /// ElementId read through <see cref="ValueOf"/>.
        /// </summary>
        internal static ElementId FromValue(long id)
        {
#if R24
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        /// <summary>
        /// The numeric value of an ElementId, or -1 when the id is null
        /// (an absent optional id reads as the invalid id, matching
        /// IntegerValue on 2023 and earlier).
        /// </summary>
        internal static long ValueOf(ElementId? id)
        {
#if R24
            return id?.Value ?? -1;
#else
            return id?.IntegerValue ?? -1;
#endif
        }
    }
}
