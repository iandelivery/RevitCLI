using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Mechanical
{
    /// <summary>
    /// Shared helpers for duct handlers: type/system resolution, connector
    /// matching, size reading (round vs rectangular/oval), geometric
    /// validation, and snapshot projection. All methods are pure (no side
    /// effects on the document) unless explicitly noted.
    /// </summary>
    internal static class DuctUtils
    {
        /// <summary>
        /// Minimum segment length accepted by <c>Duct.Create</c>: 1/10 inch in feet.
        /// Shorter segments throw ArgumentException at creation time.
        /// </summary>
        public const double MinSegmentLengthFeet = (1.0 / 12.0) / 10.0;

        /// <summary>
        /// Angle tolerance (degrees) for tee/cross perpendicularity checks.
        /// Revit's NewTeeFitting throws when the branch deviates more than
        /// ~1 degree from perpendicular to the main.
        /// </summary>
        public const double PerpendicularityToleranceDeg = 1.0;

        /// <summary>
        /// Dot-product tolerance for collinearity checks. Connectors facing
        /// each other on the same line have direction dot product ≈ -1.
        /// </summary>
        public const double CollinearDotTolerance = 1e-6;

        /// <summary>
        /// Resolve a duct type by element id, falling back to the first
        /// available type in the document when <paramref name="typeId"/> is
        /// null, zero, or negative.
        /// </summary>
        public static DuctType? ResolveType(Document doc, int? typeId)
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
        public static MechanicalSystemType? ResolveSystemType(Document doc, int? systemTypeId)
        {
            if (!systemTypeId.HasValue || systemTypeId.Value <= 0)
                return null;
            return doc.GetElement(new ElementId(systemTypeId.Value)) as MechanicalSystemType;
        }

        /// <summary>
        /// Read the shape of a duct (Round / Rectangular / Oval) from its
        /// DuctType. The shape determines which size parameters are valid.
        /// </summary>
        public static ConnectorProfileType GetShape(Duct duct)
            => duct.DuctType?.Shape ?? ConnectorProfileType.Round;

        /// <summary>
        /// Read the size of a duct in millimeters. For round ducts, only
        /// <paramref name="diameterMm"/> is populated; for rectangular/oval
        /// ducts, only <paramref name="widthMm"/> and <paramref name="heightMm"/>.
        /// </summary>
        public static (double diameterMm, double widthMm, double heightMm) GetSize(Duct duct)
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
        public static object Snapshot(Duct duct, Document doc)
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

        /// <summary>
        /// Find the pair of connectors (one from each MEP curve) with the
        /// minimum origin-to-origin distance. Returns null when no pair
        /// shares the same domain (e.g. HVAC vs piping).
        /// </summary>
        public static (Connector a, Connector b)? FindClosestConnectors(MEPCurve a, MEPCurve b)
        {
            Connector? bestA = null, bestB = null;
            double minDist = double.MaxValue;

            foreach (Connector ca in a.ConnectorManager.Connectors)
            {
                foreach (Connector cb in b.ConnectorManager.Connectors)
                {
                    if (ca.Domain != cb.Domain) continue;
                    double d = ca.Origin.DistanceTo(cb.Origin);
                    if (d < minDist)
                    {
                        minDist = d;
                        bestA = ca;
                        bestB = cb;
                    }
                }
            }

            return (bestA is not null && bestB is not null) ? (bestA, bestB) : null;
        }

        /// <summary>
        /// Find the connector on <paramref name="element"/> whose origin is
        /// closest to <paramref name="reference"/>. Used for tee/cross where
        /// the relevant connector is the one nearest the intersection point.
        /// </summary>
        public static Connector? FindClosestConnector(MEPCurve element, XYZ reference)
        {
            Connector? best = null;
            double minDist = double.MaxValue;

            foreach (Connector c in element.ConnectorManager.Connectors)
            {
                double d = c.Origin.DistanceTo(reference);
                if (d < minDist)
                {
                    minDist = d;
                    best = c;
                }
            }
            return best;
        }

        /// <summary>
        /// Compute the intersection point of two location curves. Returns
        /// null when the curves do not overlap (skew, parallel, or disjoint).
        /// </summary>
        public static XYZ? ComputeIntersection(Curve? a, Curve? b)
        {
            if (a is null || b is null) return null;

            var result = new IntersectionResultArray();
            if (a.Intersect(b, out result) == SetComparisonResult.Overlap && result.Size > 0)
                return result.get_Item(0).XYZPoint;
            return null;
        }

        /// <summary>
        /// Returns the geometric direction vector of a connector (the BasisZ
        /// of its coordinate system). Connector.Direction returns a
        /// FlowDirectionType enum and is not a vector — use this helper for
        /// angle and collinearity math.
        /// </summary>
        public static XYZ GetDirection(Connector c)
            => c.CoordinateSystem.BasisZ;

        /// <summary>
        /// Check whether two connectors are collinear — i.e. they sit on the
        /// same line and face each other (direction dot product ≈ -1, since
        /// connector directions point outward from their owning element).
        /// Required for transition and union fittings.
        /// </summary>
        public static bool AreConnectorsCollinear(Connector? a, Connector? b)
        {
            if (a is null || b is null) return false;
            double dot = GetDirection(a).DotProduct(GetDirection(b));
            return Math.Abs(dot + 1.0) < CollinearDotTolerance;
        }

        /// <summary>
        /// Compute the angle (in degrees) between two connector directions.
        /// Used for elbow (2°–95° valid range) and tee/cross (≈90° required)
        /// pre-validation.
        /// </summary>
        public static double AngleBetweenDegrees(Connector a, Connector b)
        {
            double dot = GetDirection(a).DotProduct(GetDirection(b));
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Validate that a connector pair is suitable for an elbow fitting:
        /// not the same owner, same domain, and angle within 2°–95°.
        /// Returns null when valid, or an error message describing the
        /// violation.
        /// </summary>
        public static string? ValidateElbowPair(Connector a, Connector b)
        {
            if (a.Owner.Id == b.Owner.Id)
                return "Cannot create a fitting between connectors of the same element.";
            if (a.Domain != b.Domain)
                return "Connectors must be of the same domain.";
            double angle = AngleBetweenDegrees(a, b);
            if (angle < 2.0 || angle > 95.0)
                return $"Connector angle {angle:F1}° is outside the valid range for an elbow (2°–95°).";
            return null;
        }
    }

    /// <summary>
    /// Helpers for duct fitting handlers: connector pair resolution from
    /// element IDs and optional indices, plus collinearity and domain
    /// validation. Returns (c1, c2, error) tuples where error is null on
    /// success.
    /// </summary>
    internal static class DuctFittingHelper
    {
        /// <summary>
        /// Resolve two ducts by element ID and pick a connector from each.
        /// When connector indices are null, the closest pair is
        /// auto-selected; otherwise the specified index is used (0 or 1).
        /// </summary>
        public static (Connector? c1, Connector? c2, string? error) ResolveConnectorPair(
            Document doc, int elementId1, int elementId2,
            int? connectorIndex1, int? connectorIndex2)
        {
            if (elementId1 == elementId2)
                return (null, null, "Both element IDs refer to the same element. Fittings require two distinct elements.");

            var duct1 = doc.GetElement(new ElementId(elementId1)) as Duct;
            var duct2 = doc.GetElement(new ElementId(elementId2)) as Duct;
            if (duct1 is null || duct2 is null)
                return (null, null, "One or both element IDs do not refer to a duct.");

            // Auto-select closest pair when no indices provided.
            if (!connectorIndex1.HasValue && !connectorIndex2.HasValue)
            {
                var pair = DuctUtils.FindClosestConnectors(duct1, duct2);
                if (pair is null)
                    return (null, null, "No connector pair with matching domain found between the two ducts.");
                return (pair.Value.a, pair.Value.b, null);
            }

            // Explicit indices — validate range and pick.
            var c1 = PickByIndex(duct1, connectorIndex1);
            var c2 = PickByIndex(duct2, connectorIndex2);

            if (c1 is null && connectorIndex1.HasValue)
                return (null, null, $"connector_index_1={connectorIndex1.Value} is out of range (ducts have 2 connectors: 0 and 1).");
            if (c2 is null && connectorIndex2.HasValue)
                return (null, null, $"connector_index_2={connectorIndex2.Value} is out of range (ducts have 2 connectors: 0 and 1).");

            c1 ??= DuctUtils.FindClosestConnectors(duct1, duct2)?.a;
            c2 ??= DuctUtils.FindClosestConnectors(duct1, duct2)?.b;

            if (c1 is null || c2 is null)
                return (null, null, "Could not resolve a connector pair.");

            return (c1, c2, null);
        }

        private static Connector? PickByIndex(MEPCurve duct, int? index)
        {
            if (!index.HasValue) return null;
            var list = new List<Connector>();
            foreach (Connector c in duct.ConnectorManager.Connectors)
                list.Add(c);
            if (index.Value < 0 || index.Value >= list.Count)
                return null;
            return list[index.Value];
        }

        /// <summary>
        /// Validate that two connectors are collinear (same line, facing each
        /// other) and share the HVAC domain. Required for transition and
        /// union fittings. Returns null when valid, or an error message.
        /// </summary>
        public static string? ValidateCollinearPair(Connector a, Connector b)
        {
            if (a.Owner.Id == b.Owner.Id)
                return "Cannot create a fitting between connectors of the same element.";
            if (a.Domain != b.Domain)
                return "Connectors must be of the same domain.";
            if (!DuctUtils.AreConnectorsCollinear(a, b))
            {
                double angle = DuctUtils.AngleBetweenDegrees(a, b);
                return $"Connectors are not collinear (angle {angle:F1}°, expected ~180° for end-to-end joining).";
            }
            return null;
        }
    }
}
