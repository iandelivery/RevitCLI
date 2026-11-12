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
                ["level_id"] = 3001L,
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
            };

            var ex = Assert.Throws<MissingParameterException>(() => ParameterBinder.Bind<WallParams>(dict));
            Assert.Equal("start_y", ex.ParameterName);
        }

        [Fact]
        public void Bind_DoesNotThrowWhenRequiredHasDefault()
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
                ["name"] = null!,
            };

            var p = ParameterBinder.Bind<WallParams>(dict);

            Assert.Null(p.Name);
        }

        class EmptyParams
        {
            public int Unused { get; set; } = 42;
        }

        [Fact]
        public void Bind_NoParamAttributes_ReturnsDefaultInstance()
        {
            var dict = new Dictionary<string, object> { ["unused"] = 999 };

            var p = ParameterBinder.Bind<EmptyParams>(dict);

            Assert.Equal(42, p.Unused);
        }

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

        class NullableParams
        {
            [Param("level_id")]
            public int? LevelId { get; set; }

            [Param("offset")]
            public double? Offset { get; set; }

            [Param("flag")]
            public bool? Flag { get; set; }
        }

        [Fact]
        public void Bind_NullableInt_Absent_LeavesPropertyNull()
        {
            var p = ParameterBinder.Bind<NullableParams>(
                new Dictionary<string, object>());
            Assert.Null(p.LevelId);
        }

        [Fact]
        public void Bind_NullableInt_Present_ConvertsFromLong()
        {
            var dict = new Dictionary<string, object> { ["level_id"] = 42L };
            var p = ParameterBinder.Bind<NullableParams>(dict);
            Assert.Equal(42, p.LevelId);
        }

        [Fact]
        public void Bind_NullableDouble_Present_ConvertsFromInt()
        {
            var dict = new Dictionary<string, object> { ["offset"] = 7 };
            var p = ParameterBinder.Bind<NullableParams>(dict);
            Assert.Equal(7.0, p.Offset);
        }

        [Fact]
        public void Bind_NullableBool_Present_ConvertsFromTrue()
        {
            var dict = new Dictionary<string, object> { ["flag"] = true };
            var p = ParameterBinder.Bind<NullableParams>(dict);
            Assert.True(p.Flag);
        }

        [Fact]
        public void Bind_StringValueForInt_ParsesSuccessfully()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = "3001",
            };
            var p = ParameterBinder.Bind<WallParams>(dict);
            Assert.Equal(3001, p.LevelId);
        }

        [Fact]
        public void Bind_StringValueForDouble_ParsesSuccessfully()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = "1.5", ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
            };
            var p = ParameterBinder.Bind<WallParams>(dict);
            Assert.Equal(1.5, p.StartX);
        }

        [Fact]
        public void Bind_BoolFromString_True_IsTrue()
        {
            var dict = new Dictionary<string, object> { ["include_hidden"] = "true" };
            var p = ParameterBinder.Bind<BoolParams>(dict);
            Assert.True(p.IncludeHidden);
        }

        [Fact]
        public void Bind_BoolFromString_False_IsFalse()
        {
            var dict = new Dictionary<string, object> { ["include_hidden"] = "false" };
            var p = ParameterBinder.Bind<BoolParams>(dict);
            Assert.False(p.IncludeHidden);
        }

        [Fact]
        public void Bind_BoolFromInvalidString_ThrowsTypeException()
        {
            var dict = new Dictionary<string, object> { ["include_hidden"] = "yes" };
            var ex = Assert.Throws<ParameterTypeException>(() => ParameterBinder.Bind<BoolParams>(dict));
            Assert.Equal("include_hidden", ex.ParameterName);
            Assert.Equal(typeof(bool), ex.TargetType);
        }

        [Fact]
        public void Bind_IntArray_FromActualIntArray()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
                ["tag_ids"] = new[] { 1, 2, 3 },
            };
            var p = ParameterBinder.Bind<WallParams>(dict);
            Assert.Equal(new[] { 1, 2, 3 }, p.TagIds);
        }

        [Fact]
        public void Bind_IntArray_FromMixedLongAndInt_PreservesAllValues()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
                ["tag_ids"] = new List<object> { 1, 2L, 3 },
            };
            var p = ParameterBinder.Bind<WallParams>(dict);
            Assert.Equal(new[] { 1, 2, 3 }, p.TagIds);
        }

        [Fact]
        public void Bind_IntArray_FromSingleInt_WrappedAsSingleElement()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
                ["tag_ids"] = new List<object> { 999 },
            };
            var p = ParameterBinder.Bind<WallParams>(dict);
            Assert.Equal(new[] { 999 }, p.TagIds);
        }

        [Fact]
        public void Bind_IntArray_FromEmptyList_ProducesEmptyArray()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
                ["tag_ids"] = new List<object>(),
            };
            var p = ParameterBinder.Bind<WallParams>(dict);
            Assert.NotNull(p.TagIds);
            Assert.Empty(p.TagIds);
        }

        [Fact]
        public void Bind_IntArray_FromNonConvertibleElement_ThrowsTypeException()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
                ["tag_ids"] = new List<object> { 1, "abc", 3 },
            };
            var ex = Assert.Throws<ParameterTypeException>(() => ParameterBinder.Bind<WallParams>(dict));
            Assert.Equal("tag_ids", ex.ParameterName);
            Assert.Equal(typeof(int[]), ex.TargetType);
        }

        [Fact]
        public void Bind_IntArray_FromString_ThrowsTypeException()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["end_x"] = 1.0, ["end_y"] = 1.0,
                ["level_id"] = 3,
                ["tag_ids"] = "1,2,3",
            };
            var ex = Assert.Throws<ParameterTypeException>(() => ParameterBinder.Bind<WallParams>(dict));
            Assert.Equal("tag_ids", ex.ParameterName);
        }

        class RequiredWithDefaultParams
        {
            [Param("height", Required = true, Default = 3000.0)]
            public double Height { get; set; }
        }

        [Fact]
        public void Bind_RequiredWithDefault_Absent_DoesNotThrow()
        {
            var p = ParameterBinder.Bind<RequiredWithDefaultParams>(
                new Dictionary<string, object>());
            Assert.Equal(3000.0, p.Height);
        }

        [Fact]
        public void Bind_NonDictionaryObject_TreatedAsEmpty_NoErrorsForNoParams()
        {
            var p = ParameterBinder.Bind<EmptyParams>("not a dictionary");
            Assert.Equal(42, p.Unused);
        }

        [Fact]
        public void Bind_NonDictionaryObjectWithRequiredParams_ThrowsMissing()
        {
            var ex = Assert.Throws<MissingParameterException>(
                () => ParameterBinder.Bind<WallParams>("not a dict"));
            Assert.Equal("start_x", ex.ParameterName);
        }
    }
}
