using System;
using Autodesk.Revit.DB;

namespace RevitCliBridge.Handlers.Mep
{
    /// <summary>
    /// Discipline-agnostic geometry and connector math shared by every MEP
    /// curve discipline (Duct / CableTray / Pipe, and later Conduit). Every
    /// member here operates only on MEPCurve / Connector / Curve / XYZ — never
    /// on a discipline-specific type — so the logic is written exactly once
    /// for all three instead of being copied per namespace (the historical
    /// ~90% duplication between DuctUtils and CableTrayUtils).
    /// </summary>
    internal static class MepCurveGeometry
    {
        /// <summary>
        /// Minimum segment length accepted by the MEP <c>Xxx.Create</c>
        /// methods: 1/10 inch in feet. Shorter segments throw ArgumentException
        /// at creation time.
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

        /// <summary>
        /// Validate that two connectors are collinear (same line, facing each
        /// other). Required for transition and union fittings. Returns null
        /// when valid, or an error message.
        /// </summary>
        public static string? ValidateCollinearPair(Connector a, Connector b)
        {
            if (a.Owner.Id == b.Owner.Id)
                return "Cannot create a fitting between connectors of the same element.";
            if (a.Domain != b.Domain)
                return "Connectors must be of the same domain.";
            if (!AreConnectorsCollinear(a, b))
            {
                double angle = AngleBetweenDegrees(a, b);
                return $"Connectors are not collinear (angle {angle:F1}°, expected ~180° for end-to-end joining).";
            }
            return null;
        }
    }
}