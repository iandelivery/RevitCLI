using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Electrical
{
    /// <summary>
    /// Shared helpers for cable tray handlers: type resolution, connector
    /// matching, size reading, geometric validation, and snapshot projection.
    /// All methods are pure (no side effects on the document) and safe to
    /// call from a read-only context unless explicitly noted.
    /// </summary>
    internal static class CableTrayUtils
    {
        /// <summary>
        /// Minimum segment length accepted by <c>CableTray.Create</c>: 1/10 inch in feet.
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
        /// Resolve a cable tray type by element id, falling back to the first
        /// available type in the document when <paramref name="typeId"/> is
        /// null, zero, or negative.
        /// </summary>
        public static CableTrayType? ResolveType(Document doc, int? typeId)
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
        public static (double widthMm, double heightMm) GetSize(CableTray ct)
        {
            double widthFt = ct.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? 0.0;
            double heightFt = ct.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? 0.0;
            return (widthFt.FeetToMillimeter(), heightFt.FeetToMillimeter());
        }

        /// <summary>
        /// Project a cable tray into a serializable snapshot with all
        /// coordinates converted to millimeters.
        /// </summary>
        public static object Snapshot(CableTray ct, Document doc)
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

        /// <summary>
        /// Find the pair of connectors (one from each MEP curve) with the
        /// minimum origin-to-origin distance. Returns null when no pair
        /// shares the same domain (e.g. electrical vs piping).
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
            // Clamp to [-1, 1] to avoid NaN from floating-point drift.
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
}
