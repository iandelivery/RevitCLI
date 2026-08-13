using System.Collections.Generic;
using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for ParameterBinder — the reflective POCO binder that replaces
    /// per-handler HandlerUtilities.GetXxxOrNull boilerplate.
    /// </summary>
    public class ParameterBinderTests
    {
        // Test POCO — mirrors the shape of a real handler's parameter set.
        class WallParams
        {
            [Param("start_x", Required = true)]
            public double StartX { get; set; }

            [Param("start_y", Required = true)]
            public double StartY { get; set; }

            [Param("end_x", Required = true)]
            public double EndX { get; set; }

            [Param("end_y", Required = true)]
            public double EndY { get; set; }

            [Param("level_id", Required = true)]
            public int LevelId { get; set; }

            [Param("height", Default = 3000.0)]
            public double Height { get; set; }

            [Param("name")]
            public string? Name { get; set; }

            [Param("tag_ids")]
            public int[]? TagIds { get; set; }
        }

        [Fact]
        public void Bind_PopulatesRequiredFields()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 100.0,
                ["start_y"] = 200.0,
                ["end_x"] = 300.0,
                ["end_y"] = 400.0,
                ["level_id"] = 3001L, // JSON deserializer yields long
            };

            var p = ParameterBinder.Bind<WallParams>(dict);

            Assert.Equal(100.0, p.StartX);
            Assert.Equal(200.0, p.StartY);
            Assert.Equal(300.0, p.EndX);
            Assert.Equal(400.0, p.EndY);
            Assert.Equal(3001, p.LevelId);
        }

        [Fact]
        public void Bind_AppliesDefaultWhenOptionalAbsent()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
            };

            var p = ParameterBinder.Bind<WallParams>(dict);

            Assert.Equal(3000.0, p.Height);
        }

        [Fact]
        public void Bind_OverrideDefaultWhenProvided()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
                ["height"] = 2800.0,
            };

            var p = ParameterBinder.Bind<WallParams>(dict);

            Assert.Equal(2800.0, p.Height);
        }

        [Fact]
        public void Bind_LeavesStringNullWhenAbsent()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
            };

            var p = ParameterBinder.Bind<WallParams>(dict);

            Assert.Null(p.Name);
        }

        [Fact]
        public void Bind_PopulatesStringWhenProvided()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
                ["name"] = "north wall",
            };

            var p = ParameterBinder.Bind<WallParams>(dict);

            Assert.Equal("north wall", p.Name);
        }

        [Fact]
        public void Bind_ConvertsIntArrayOfLongs()
        {
            // JSON deserializer produces List<object> of long, not int[].
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
                ["tag_ids"] = new List<object> { 100L, 200L, 300L },
            };

            var p = ParameterBinder.Bind<WallParams>(dict);

            Assert.Equal(new[] { 100, 200, 300 }, p.TagIds);
        }

        [Fact]
        public void Bind_ThrowsMissingParameterForRequiredAbsent()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0,
                // missing start_y, end_x, end_y, level_id
            };

            var ex = Assert.Throws<MissingParameterException>(() => ParameterBinder.Bind<WallParams>(dict));
            // The first missing required parameter encountered should be reported.
            Assert.Equal("start_y", ex.ParameterName);
        }

        [Fact]
        public void Bind_DoesNotThrowWhenRequiredHasDefault()
        {
            // "height" has Default=3000.0 — if we marked it Required=true
            // (it's not in this POCO, but verify the default-applied-first rule
            // by checking that a missing optional-with-default doesn't throw).
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
            };

            var p = ParameterBinder.Bind<WallParams>(dict); // should not throw
            Assert.Equal(3000.0, p.Height);
        }

        [Fact]
        public void Bind_ThrowsTypeExceptionForBadInt()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = "not-an-int",
            };

            var ex = Assert.Throws<ParameterTypeException>(() => ParameterBinder.Bind<WallParams>(dict));
            Assert.Equal("level_id", ex.ParameterName);
            Assert.Equal(typeof(int), ex.TargetType);
        }

        [Fact]
        public void Bind_ThrowsTypeExceptionForBadDouble()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = "abc", ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
            };

            var ex = Assert.Throws<ParameterTypeException>(() => ParameterBinder.Bind<WallParams>(dict));
            Assert.Equal("start_x", ex.ParameterName);
        }

        [Fact]
        public void Bind_HandlesNullDictionary()
        {
            var ex = Assert.Throws<MissingParameterException>(() => ParameterBinder.Bind<WallParams>(null));
            Assert.Equal("start_x", ex.ParameterName);
        }

        [Fact]
        public void Bind_HandlesEmptyDictionary()
        {
            var ex = Assert.Throws<MissingParameterException>(() => ParameterBinder.Bind<WallParams>(new Dictionary<string, object>()));
            Assert.Equal("start_x", ex.ParameterName);
        }

        [Fact]
        public void Bind_AcceptsLooseObjectParameter()
        {
            // QueuedCommand.Parameters is typed as object?. but usually holds Dictionary.
            object dict = new Dictionary<string, object>
            {
                ["start_x"] = 1.0, ["start_y"] = 2.0, ["end_x"] = 3.0, ["end_y"] = 4.0,
                ["level_id"] = 5,
            };

            var p = ParameterBinder.Bind<WallParams>(dict);

            Assert.Equal(1.0, p.StartX);
            Assert.Equal(5, p.LevelId);
        }

        [Fact]
        public void Bind_TreatsNullParameterValueAsAbsent()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
                ["name"] = null!, // should be treated as absent, leaving Name=null
            };

            var p = ParameterBinder.Bind<WallParams>(dict);

            Assert.Null(p.Name);
        }

        // POCO with no [Param] attributes — should bind to defaults without error.
        class EmptyParams
        {
            public int Unused { get; set; } = 42;
        }

        [Fact]
        public void Bind_NoParamAttributes_ReturnsDefaultInstance()
        {
            var dict = new Dictionary<string, object> { ["unused"] = 999 };

            var p = ParameterBinder.Bind<EmptyParams>(dict);

            // No [Param] on Unused, so it's not touched.
            Assert.Equal(42, p.Unused);
        }

        // POCO using snake_case auto-name (no explicit Name in [Param]).
        class AutoNameParams
        {
            [Param(Required = true)]
            public int ElementId { get; set; }

            [Param]
            public string? ViewName { get; set; }
        }

        [Fact]
        public void Bind_AutoSnakeCaseNameFromPropertyName()
        {
            var dict = new Dictionary<string, object>
            {
                ["element_id"] = 42,
                ["view_name"] = "Level 1",
            };

            var p = ParameterBinder.Bind<AutoNameParams>(dict);

            Assert.Equal(42, p.ElementId);
            Assert.Equal("Level 1", p.ViewName);
        }

        // POCO with bool.
        class BoolParams
        {
            [Param("include_hidden")]
            public bool IncludeHidden { get; set; }

            [Param("dry_run", Default = false)]
            public bool DryRun { get; set; }
        }

        [Fact]
        public void Bind_ConvertsBool()
        {
            var dict = new Dictionary<string, object>
            {
                ["include_hidden"] = true,
            };

            var p = ParameterBinder.Bind<BoolParams>(dict);

            Assert.True(p.IncludeHidden);
            Assert.False(p.DryRun);
        }
    }
}
