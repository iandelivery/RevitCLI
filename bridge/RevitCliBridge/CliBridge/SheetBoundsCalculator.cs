using System;

namespace RevitCliBridge
{
    /// <summary>
    /// An axis-aligned rectangle in sheet coordinates. Values carry the
    /// caller's units; the sheet viewport handlers convert feet to
    /// millimeters before they call this calculator.
    /// </summary>
    public readonly struct SheetRect
    {
        /// <summary>Lower-left X.</summary>
        public double MinX { get; }

        /// <summary>Lower-left Y.</summary>
        public double MinY { get; }

        /// <summary>Upper-right X.</summary>
        public double MaxX { get; }

        /// <summary>Upper-right Y.</summary>
        public double MaxY { get; }

        public SheetRect(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }
    }

    /// <summary>
    /// The distance per edge by which a rectangle crosses the boundary of a
    /// region. Zero means that edge sits inside the region.
    /// </summary>
    public readonly struct EdgeOverflow
    {
        /// <summary>Distance the box left edge sits left of the region.</summary>
        public double Left { get; }

        /// <summary>Distance the box right edge sits right of the region.</summary>
        public double Right { get; }

        /// <summary>Distance the box top edge sits above the region.</summary>
        public double Top { get; }

        /// <summary>Distance the box bottom edge sits below the region.</summary>
        public double Bottom { get; }

        public EdgeOverflow(double left, double right, double top, double bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }
    }

    /// <summary>
    /// Pure sheet-space geometry for the sheet viewport commands: shrink a
    /// bound rectangle by per-edge margins, test containment, measure
    /// per-edge overflow, and intersect rectangle pairs. No Revit API
    /// dependency, so the test project links this file directly.
    /// </summary>
    public static class SheetBoundsCalculator
    {
        /// <summary>
        /// Shrinks a rectangle by nonnegative margins. The left margin moves
        /// the MinX edge inward, the right margin moves the MaxX edge inward,
        /// and likewise for bottom with MinY and top with MaxY.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Any margin is negative.
        /// </exception>
        public static SheetRect Shrink(SheetRect rect, double left, double right, double top, double bottom)
        {
            if (left < 0 || right < 0 || top < 0 || bottom < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(left), "Margins must be nonnegative.");

            return new SheetRect(
                rect.MinX + left,
                rect.MinY + bottom,
                rect.MaxX - right,
                rect.MaxY - top);
        }

        /// <summary>
        /// True when the box lies inside the region or touches its edge.
        /// </summary>
        public static bool Contains(SheetRect region, SheetRect box)
        {
            return box.MinX >= region.MinX
                && box.MaxX <= region.MaxX
                && box.MinY >= region.MinY
                && box.MaxY <= region.MaxY;
        }

        /// <summary>
        /// Measures how far the box crosses the region on each edge. An edge
        /// that sits inside yields zero. The four values feed a move command
        /// directly: shifting the box by the reported overflow pulls it in.
        /// </summary>
        public static EdgeOverflow OverflowAgainst(SheetRect region, SheetRect box)
        {
            return new EdgeOverflow(
                Math.Max(0, region.MinX - box.MinX),
                Math.Max(0, box.MaxX - region.MaxX),
                Math.Max(0, box.MaxY - region.MaxY),
                Math.Max(0, region.MinY - box.MinY));
        }

        /// <summary>
        /// Intersects two rectangles. Returns null when the rectangles are
        /// disjoint or share only an edge or a corner, which is no overlap.
        /// </summary>
        public static SheetRect? Intersect(SheetRect a, SheetRect b)
        {
            var minX = Math.Max(a.MinX, b.MinX);
            var minY = Math.Max(a.MinY, b.MinY);
            var maxX = Math.Min(a.MaxX, b.MaxX);
            var maxY = Math.Min(a.MaxY, b.MaxY);

            if (minX >= maxX || minY >= maxY)
                return null;

            return new SheetRect(minX, minY, maxX, maxY);
        }

        /// <summary>Rectangle area in squared caller units.</summary>
        public static double Area(SheetRect rect)
        {
            return (rect.MaxX - rect.MinX) * (rect.MaxY - rect.MinY);
        }
    }
}
