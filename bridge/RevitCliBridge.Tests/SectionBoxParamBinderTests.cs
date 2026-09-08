using System.Collections.Generic;
using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for the parameter binding contracts of the section box
    /// commands (set_section_box and toggle_section_box). Mirrors the
    /// SetSectionBoxParams and ToggleSectionBoxParams POCOs declared in
    /// the Views handler file (which itself cannot be linked here because
    /// it depends on the Revit API). Verifies that bounds are optional
    /// nullable doubles (so the element_ids mode can omit them), the
    /// toggle command's enable flag defaults to true, and numeric
    /// coercion works for both long (JSON) and double inputs.
    /// </summary>
    public class SectionBoxParamBinderTests
    {
        // ---------- POCOs mirroring the real handler parameter bags ----------

        class SetSectionBoxParams
        {
            [Param("view_id")]
            public int? ViewId { get; set; }

            [Param("element_ids")]
            public int[]? ElementIds { get; set; }

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

        class ToggleSectionBoxParams
        {
            [Param("view_id")]
            public int? ViewId { get; set; }

            [Param("enable", Default = true)]
            public bool Enable { get; set; }
        }

        // ---------- set_section_box ----------

        [Fact]
        public void SetSectionBox_Bind_EmptyParameters_AllOptional()
        {
            var p = ParameterBinder.Bind<SetSectionBoxParams>(new Dictionary<string, object>());

            Assert.Null(p.ViewId);
            Assert.Null(p.ElementIds);
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
            Assert.Null(p.ElementIds);
            Assert.Equal(0.0, p.MinX);
            Assert.Equal(10000.0, p.MaxX);
            Assert.Equal(4000.0, p.MaxZ);
        }

        [Fact]
        public void SetSectionBox_Bind_ElementIds()
        {
            var dict = new Dictionary<string, object>
            {
                ["element_ids"] = new List<object> { 12345L, 12346L, 12347L },
            };

            var p = ParameterBinder.Bind<SetSectionBoxParams>(dict);

            Assert.Equal(new[] { 12345, 12346, 12347 }, p.ElementIds);
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
        public void SetSectionBox_Bind_NonIntElementIds_Throws()
        {
            var dict = new Dictionary<string, object>
            {
                ["element_ids"] = new List<object> { 12345L, "not-an-int" },
            };

            var ex = Assert.Throws<ParameterTypeException>(
                () => ParameterBinder.Bind<SetSectionBoxParams>(dict));
            Assert.Equal("element_ids", ex.ParameterName);
        }

        // ---------- toggle_section_box ----------

        [Fact]
        public void ToggleSectionBox_Bind_EmptyParameters_EnableDefaultsTrue()
        {
            var p = ParameterBinder.Bind<ToggleSectionBoxParams>(new Dictionary<string, object>());

            Assert.True(p.Enable);
            Assert.Null(p.ViewId);
        }

        [Fact]
        public void ToggleSectionBox_Bind_EnableFalse()
        {
            var dict = new Dictionary<string, object>
            {
                ["view_id"] = 12345L,
                ["enable"] = false,
            };

            var p = ParameterBinder.Bind<ToggleSectionBoxParams>(dict);

            Assert.False(p.Enable);
            Assert.Equal(12345, p.ViewId);
        }

        [Fact]
        public void ToggleSectionBox_Bind_NonBoolEnable_Throws()
        {
            var dict = new Dictionary<string, object>
            {
                ["enable"] = "not-a-bool",
            };

            var ex = Assert.Throws<ParameterTypeException>(
                () => ParameterBinder.Bind<ToggleSectionBoxParams>(dict));
            Assert.Equal("enable", ex.ParameterName);
        }

        [Fact]
        public void ToggleSectionBox_Bind_NullParameters_EnableDefaultsTrue()
        {
            var p = ParameterBinder.Bind<ToggleSectionBoxParams>(null);

            Assert.True(p.Enable);
            Assert.Null(p.ViewId);
        }
    }
}
