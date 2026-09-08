using System.Collections.Generic;
using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for the parameter binding contract of the set_section_box
    /// command. Mirrors the SetSectionBoxParams POCO declared in the Views
    /// handler file (which itself cannot be linked here because it depends
    /// on the Revit API). Verifies that the enable flag defaults to true,
    /// bounds are optional nullable doubles, and numeric coercion works
    /// for both long (JSON) and double inputs.
    /// </summary>
    public class SectionBoxParamBinderTests
    {
        // ---------- POCO mirroring the real handler parameter bag ----------

        class SetSectionBoxParams
        {
            [Param("view_id")]
            public int? ViewId { get; set; }

            [Param("enable", Default = true)]
            public bool Enable { get; set; }

            [Param("min_x")]
            public double? MinX { get; set; }

            [Param("min_y")]
            public double? MinY { get; set; }

            [Param("min_z")]
            public double? MinZ { get; set; }

            [Param("max_x")]
            public double? MaxX { get; set; }

            [Param("max_y")]
            public double? MaxY { get; set; }

            [Param("max_z")]
            public double? MaxZ { get; set; }
        }

        // ---------- set_section_box ----------

        [Fact]
        public void SetSectionBox_Bind_EmptyParameters_EnableDefaultsTrue()
        {
            var p = ParameterBinder.Bind<SetSectionBoxParams>(new Dictionary<string, object>());

            Assert.True(p.Enable);
            Assert.Null(p.ViewId);
            Assert.Null(p.MinX);
            Assert.Null(p.MaxZ);
        }

        [Fact]
        public void SetSectionBox_Bind_FullBounds()
        {
            var dict = new Dictionary<string, object>
            {
                ["view_id"] = 12345L,
                ["min_x"] = 0.0, ["min_y"] = 0.0, ["min_z"] = 0.0,
                ["max_x"] = 10000.0, ["max_y"] = 8000.0, ["max_z"] = 4000.0,
            };

            var p = ParameterBinder.Bind<SetSectionBoxParams>(dict);

            Assert.Equal(12345, p.ViewId);
            Assert.True(p.Enable);
            Assert.Equal(0.0, p.MinX);
            Assert.Equal(10000.0, p.MaxX);
            Assert.Equal(4000.0, p.MaxZ);
        }

        [Fact]
        public void SetSectionBox_Bind_EnableFalse()
        {
            var dict = new Dictionary<string, object>
            {
                ["view_id"] = 12345L,
                ["enable"] = false,
            };

            var p = ParameterBinder.Bind<SetSectionBoxParams>(dict);

            Assert.False(p.Enable);
            Assert.Null(p.MinX);
            Assert.Null(p.MaxZ);
        }

        [Fact]
        public void SetSectionBox_Bind_LongValuesCoercedToDouble()
        {
            // JSON deserialization produces longs for integer literals —
            // the binder must coerce them to the nullable double bounds.
            var dict = new Dictionary<string, object>
            {
                ["min_x"] = 0L, ["min_y"] = 0L, ["min_z"] = 0L,
                ["max_x"] = 10000L, ["max_y"] = 8000L, ["max_z"] = 4000L,
            };

            var p = ParameterBinder.Bind<SetSectionBoxParams>(dict);

            Assert.Equal(0.0, p.MinX);
            Assert.Equal(10000.0, p.MaxX);
            Assert.Equal(8000.0, p.MaxY);
            Assert.Equal(4000.0, p.MaxZ);
        }

        [Fact]
        public void SetSectionBox_Bind_NonNumericBound_Throws()
        {
            var dict = new Dictionary<string, object>
            {
                ["min_x"] = "not-a-number",
            };

            var ex = Assert.Throws<ParameterTypeException>(
                () => ParameterBinder.Bind<SetSectionBoxParams>(dict));
            Assert.Equal("min_x", ex.ParameterName);
        }

        [Fact]
        public void SetSectionBox_Bind_NonBoolEnable_Throws()
        {
            var dict = new Dictionary<string, object>
            {
                ["enable"] = "not-a-bool",
            };

            var ex = Assert.Throws<ParameterTypeException>(
                () => ParameterBinder.Bind<SetSectionBoxParams>(dict));
            Assert.Equal("enable", ex.ParameterName);
        }

        [Fact]
        public void SetSectionBox_Bind_NullParameters_EnableDefaultsTrue()
        {
            var p = ParameterBinder.Bind<SetSectionBoxParams>(null);

            Assert.True(p.Enable);
            Assert.Null(p.ViewId);
        }
    }
}
