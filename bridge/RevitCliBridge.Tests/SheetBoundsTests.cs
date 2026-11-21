using System;
using Newtonsoft.Json.Linq;
using RevitCliBridge;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for <see cref="SheetBoundsCalculator"/> and
    /// <see cref="SheetPlacementParser"/> — the pure geometry and array
    /// parsing behind the sheet viewport commands.
    /// </summary>
    public class SheetBoundsTests
    {
        public class Calculator
        {
            [Fact]
            public void Shrink_MovesEachEdgeInward()
            {
                var rect = new SheetRect(0, 0, 100, 50);

                var shrunk = SheetBoundsCalculator.Shrink(rect, 10, 20, 5, 15);

                Assert.Equal(10, shrunk.MinX);
                Assert.Equal(15, shrunk.MinY);
                Assert.Equal(80, shrunk.MaxX);
                Assert.Equal(45, shrunk.MaxY);
            }

            [Fact]
            public void Shrink_RejectsAnyNegativeMargin()
            {
                var rect = new SheetRect(0, 0, 100, 50);

                Assert.Throws<ArgumentOutOfRangeException>(
                    () => SheetBoundsCalculator.Shrink(rect, -1, 0, 0, 0));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => SheetBoundsCalculator.Shrink(rect, 0, 0, 0, -0.5));
            }

            [Fact]
            public void Contains_TrueForInsideAndTouching()
            {
                var region = new SheetRect(0, 0, 100, 50);

                Assert.True(SheetBoundsCalculator.Contains(region, new SheetRect(0, 0, 100, 50)));
                Assert.True(SheetBoundsCalculator.Contains(region, new SheetRect(10, 10, 90, 40)));
            }

            [Fact]
            public void Contains_FalseWhenAnyEdgeCrosses()
            {
                var region = new SheetRect(0, 0, 100, 50);

                Assert.False(SheetBoundsCalculator.Contains(region, new SheetRect(-1, 0, 100, 50)));
                Assert.False(SheetBoundsCalculator.Contains(region, new SheetRect(0, 0, 101, 50)));
                Assert.False(SheetBoundsCalculator.Contains(region, new SheetRect(0, 51, 100, 60)));
            }

            [Fact]
            public void Overflow_ReportsPerEdgeDistance()
            {
                var region = new SheetRect(10, 10, 90, 45);
                var box = new SheetRect(5, 5, 95, 40);

                var overflow = SheetBoundsCalculator.OverflowAgainst(region, box);

                Assert.Equal(5, overflow.Left);
                Assert.Equal(5, overflow.Right);
                Assert.Equal(0, overflow.Top);
                Assert.Equal(5, overflow.Bottom);
            }

            [Fact]
            public void Overflow_AllZeroWhenInside()
            {
                var region = new SheetRect(0, 0, 100, 50);

                var overflow = SheetBoundsCalculator.OverflowAgainst(
                    region, new SheetRect(10, 10, 90, 40));

                Assert.Equal(0, overflow.Left);
                Assert.Equal(0, overflow.Right);
                Assert.Equal(0, overflow.Top);
                Assert.Equal(0, overflow.Bottom);
            }

            [Fact]
            public void Intersect_ReturnsIntersectionRectangle()
            {
                var a = new SheetRect(0, 0, 100, 50);
                var b = new SheetRect(50, 25, 150, 75);

                var overlap = SheetBoundsCalculator.Intersect(a, b);

                Assert.NotNull(overlap);
                Assert.Equal(50, overlap!.Value.MinX);
                Assert.Equal(25, overlap.Value.MinY);
                Assert.Equal(100, overlap.Value.MaxX);
                Assert.Equal(50, overlap.Value.MaxY);
            }

            [Fact]
            public void Intersect_NullForDisjointAndEdgeTouching()
            {
                var a = new SheetRect(0, 0, 100, 50);

                Assert.Null(SheetBoundsCalculator.Intersect(a, new SheetRect(100, 0, 200, 50)));
                Assert.Null(SheetBoundsCalculator.Intersect(a, new SheetRect(150, 60, 200, 80)));
            }

            [Fact]
            public void Area_MultipliesSides()
            {
                Assert.Equal(200, SheetBoundsCalculator.Area(new SheetRect(0, 0, 20, 10)));
            }
        }

        public class PlacementParser
        {
            [Fact]
            public void TryParse_ReadsIdAndCoordinates()
            {
                var raw = JArray.Parse(@"[{""view_id"": 3113, ""x"": 300, ""y"": 200}]");

                var ok = SheetPlacementParser.TryParse(
                    raw, "views", "view_id", true, out var items, out var error);

                Assert.True(ok);
                Assert.Null(error);
                var item = Assert.Single(items);
                Assert.Equal(3113, item.Id);
                Assert.Equal(300, item.X);
                Assert.Equal(200, item.Y);
            }

            [Fact]
            public void TryParse_AllowsOmittedCoordinatesWhenNotRequired()
            {
                var raw = JArray.Parse(@"[{""view_id"": 3113}]");

                var ok = SheetPlacementParser.TryParse(
                    raw, "views", "view_id", false, out var items, out var error);

                Assert.True(ok);
                Assert.Null(error);
                var item = Assert.Single(items);
                Assert.Null(item.X);
                Assert.Null(item.Y);
            }

            [Fact]
            public void TryParse_RejectsMissingCoordinatesWhenRequired()
            {
                var raw = JArray.Parse(@"[{""viewport_id"": 7}]");

                var ok = SheetPlacementParser.TryParse(
                    raw, "moves", "viewport_id", true, out _, out var error);

                Assert.False(ok);
                Assert.Equal("'moves' item 0 is missing 'x' or 'y'.", error);
            }

            [Fact]
            public void TryParse_RejectsHalfSetCoordinates()
            {
                var raw = JArray.Parse(@"[{""view_id"": 3113, ""x"": 300}]");

                var ok = SheetPlacementParser.TryParse(
                    raw, "views", "view_id", false, out _, out var error);

                Assert.False(ok);
                Assert.Equal("'views' item 0 must set both 'x' and 'y'.", error);
            }

            [Fact]
            public void TryParse_RejectsNonNumericCoordinate()
            {
                var raw = JArray.Parse(@"[{""view_id"": 3113, ""x"": ""abc"", ""y"": 200}]");

                var ok = SheetPlacementParser.TryParse(
                    raw, "views", "view_id", true, out _, out var error);

                Assert.False(ok);
                Assert.Equal("'views' item 0 needs numeric 'x' and 'y'.", error);
            }

            [Fact]
            public void TryParse_RejectsMissingId()
            {
                var raw = JArray.Parse(@"[{""x"": 300, ""y"": 200}]");

                var ok = SheetPlacementParser.TryParse(
                    raw, "views", "view_id", true, out _, out var error);

                Assert.False(ok);
                Assert.Equal("'views' item 0 needs an integer 'view_id'.", error);
            }

            [Fact]
            public void TryParse_RejectsNonObjectItem()
            {
                var raw = JArray.Parse(@"[42]");

                var ok = SheetPlacementParser.TryParse(
                    raw, "views", "view_id", true, out _, out var error);

                Assert.False(ok);
                Assert.Equal("'views' item 0 must be an object.", error);
            }

            [Fact]
            public void TryParse_RejectsNonArray()
            {
                var ok = SheetPlacementParser.TryParse(
                    JObject.Parse(@"{""view_id"": 1}"), "views", "view_id", true, out _, out var error);

                Assert.False(ok);
                Assert.Equal("'views' must be an array of objects.", error);
            }

            [Fact]
            public void TryParse_RejectsEmptyArray()
            {
                var raw = new JArray();

                var ok = SheetPlacementParser.TryParse(
                    raw, "views", "view_id", true, out _, out var error);

                Assert.False(ok);
                Assert.Equal("'views' must contain at least one item.", error);
            }

            [Fact]
            public void TryParse_ReadsMultipleItemsWithIndexInError()
            {
                var raw = JArray.Parse(
                    @"[{""view_id"": 1, ""x"": 0, ""y"": 0}, {""view_id"": 2}]");

                var ok = SheetPlacementParser.TryParse(
                    raw, "views", "view_id", true, out _, out var error);

                Assert.False(ok);
                Assert.Equal("'views' item 1 is missing 'x' or 'y'.", error);
            }
        }
    }
}
