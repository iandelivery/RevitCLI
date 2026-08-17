using System;
using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for <see cref="ParamAttribute"/> and the two parameter-binding
    /// exceptions (<see cref="MissingParameterException"/> and
    /// <see cref="ParameterTypeException"/>). Verifies attribute defaults,
    /// usage constraints, and exception message format — these are the
    /// user-facing API contract for handler authors.
    /// </summary>
    public class ParamAttributeTests
    {
        // ---------- ParamAttribute defaults & behavior ----------

        [Fact]
        public void ParamAttribute_Default_NameIsEmpty_RequiredFalse_DefaultNull()
        {
            var attr = new ParamAttribute();
            Assert.Equal(string.Empty, attr.Name);
            Assert.False(attr.Required);
            Assert.Null(attr.Default);
        }

        [Fact]
        public void ParamAttribute_WithName_SetsName()
        {
            var attr = new ParamAttribute("level_id");
            Assert.Equal("level_id", attr.Name);
        }

        [Fact]
        public void ParamAttribute_NullName_FallsBackToEmpty()
        {
            // Constructor explicitly null-coerces to empty string.
            var attr = new ParamAttribute(null!);
            Assert.Equal(string.Empty, attr.Name);
        }

        [Fact]
        public void ParamAttribute_RequiredTrueAndDefault_PreservesBoth()
        {
            var attr = new ParamAttribute("height")
            {
                Required = true,
                Default = 3000.0
            };
            Assert.True(attr.Required);
            Assert.Equal(3000.0, attr.Default);
        }

        // ---------- AttributeUsage contract ----------

        [Fact]
        public void ParamAttribute_CanBeAppliedToProperty_OnlyOnce()
        {
            // The attribute is [AttributeUsage(Property, AllowMultiple=false)].
            // Reflection on a single-property POCO should yield exactly one.
            var attrs = Attribute.GetCustomAttributes(
                typeof(SingleAttrPoco).GetProperty(nameof(SingleAttrPoco.X))!,
                typeof(ParamAttribute));
            Assert.Single(attrs);
        }

        [Fact]
        public void ParamAttribute_IsInherited()
        {
            // AttributeUsage sets Inherited=true. A subclass property should
            // see the attribute applied on its base.
            var attrs = Attribute.GetCustomAttributes(
                typeof(DerivedPoco).GetProperty(nameof(DerivedPoco.X))!,
                typeof(ParamAttribute));
            Assert.Single(attrs);
        }

        private class SingleAttrPoco
        {
            [Param("x")]
            public int X { get; set; }
        }

        private class DerivedPoco : SingleAttrPoco { }

        // ---------- MissingParameterException ----------

        [Fact]
        public void MissingParameterException_Message_ContainsParameterName()
        {
            var ex = new MissingParameterException("level_id");
            Assert.Equal("level_id", ex.ParameterName);
            Assert.Contains("level_id", ex.Message);
            Assert.Contains("Missing required parameter", ex.Message);
        }

        [Fact]
        public void MissingParameterException_MessageFormat_IsStable()
        {
            // Pinned to detect accidental message wording changes that
            // would break client-side error parsing.
            var ex = new MissingParameterException("height");
            Assert.Equal("Missing required parameter: height.", ex.Message);
        }

        // ---------- ParameterTypeException ----------

        [Fact]
        public void ParameterTypeException_PreservesParameterNameAndTargetType()
        {
            var inner = new FormatException("bad number");
            var ex = new ParameterTypeException("level_id", typeof(int), inner);

            Assert.Equal("level_id", ex.ParameterName);
            Assert.Equal(typeof(int), ex.TargetType);
            Assert.Same(inner, ex.InnerException);
        }

        [Fact]
        public void ParameterTypeException_Message_ContainsParameterAndTypeName()
        {
            var ex = new ParameterTypeException("level_id", typeof(int),
                new FormatException("bad"));
            Assert.Contains("level_id", ex.Message);
            Assert.Contains("Int32", ex.Message);
        }

        [Fact]
        public void ParameterTypeException_MessageFormat_IsStable()
        {
            // Pinned format so client-side error matching doesn't silently break.
            var ex = new ParameterTypeException("level_id", typeof(int),
                new FormatException("Input string was not in a correct format."));
            Assert.Equal(
                "Parameter 'level_id' could not be converted to Int32: " +
                "Input string was not in a correct format.",
                ex.Message);
        }
    }
}
