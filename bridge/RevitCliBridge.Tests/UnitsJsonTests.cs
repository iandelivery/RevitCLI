using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RevitCliBridge;
using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for <see cref="UnitsJson"/> — the JSON half of the units
    /// middleware that converts declared-kind request parameters to internal
    /// units and rewrites declared response fields back.
    /// </summary>
    public class UnitsJsonTests
    {
        private static Func<string, string> Canonical =>
            kind => UnitContracts.CanonicalUnits[kind];

        private static Func<string, string> FtForLength =>
            kind => kind == UnitKinds.Length ? "ft" : UnitContracts.CanonicalUnits[kind];

        public class ApplyToParameters
        {
            [Fact]
            public void DeclaredKind_ConvertsMillimetersToFeet()
            {
                var parameters = new Dictionary<string, object> { ["x"] = 300.0 };
                var schemas = new[]
                {
                    new CommandParamSchema { Name = "x", Type = "double", UnitKind = UnitKinds.Length }
                };

                UnitsJson.ApplyToParameters(parameters, schemas, Canonical);

                Assert.Equal(300.0 / 304.8, Convert.ToDouble(parameters["x"]), 9);
            }

            [Fact]
            public void DeclaredKind_InFeetRequest_IsIdentity()
            {
                var parameters = new Dictionary<string, object> { ["x"] = 10.0 };
                var schemas = new[]
                {
                    new CommandParamSchema { Name = "x", Type = "double", UnitKind = UnitKinds.Length }
                };

                UnitsJson.ApplyToParameters(parameters, schemas, FtForLength);

                Assert.Equal(10.0, Convert.ToDouble(parameters["x"]), 9);
            }

            [Fact]
            public void UndeclaredParameter_IsUntouched()
            {
                var parameters = new Dictionary<string, object> { ["x"] = 300.0, ["count"] = 5L };

                UnitsJson.ApplyToParameters(parameters, Array.Empty<CommandParamSchema>(), Canonical);

                Assert.Equal(300.0, Convert.ToDouble(parameters["x"]));
                Assert.Equal(5L, parameters["count"]);
            }

            [Fact]
            public void RawKind_IsUntouched()
            {
                var parameters = new Dictionary<string, object> { ["ratio"] = 0.5 };
                var schemas = new[]
                {
                    new CommandParamSchema { Name = "ratio", Type = "double", UnitKind = UnitKinds.Raw }
                };

                UnitsJson.ApplyToParameters(parameters, schemas, Canonical);

                Assert.Equal(0.5, Convert.ToDouble(parameters["ratio"]));
            }

            [Fact]
            public void NestedProperties_ConvertInsideArrayItems()
            {
                var array = JArray.Parse(@"[{""view_id"": 1, ""x"": 300.0, ""y"": 200.0}, {""view_id"": 2}]");
                var parameters = new Dictionary<string, object> { ["views"] = array };
                var schemas = new[]
                {
                    new CommandParamSchema
                    {
                        Name = "views", Type = "object", UnitKind = UnitKinds.Length,
                        Properties = new[]
                        {
                            new CommandParamSchema { Name = "view_id", Type = "int" },
                            new CommandParamSchema { Name = "x", Type = "double", UnitKind = UnitKinds.Length },
                            new CommandParamSchema { Name = "y", Type = "double", UnitKind = UnitKinds.Length }
                        }
                    }
                };

                UnitsJson.ApplyToParameters(parameters, schemas, Canonical);

                var converted = (JArray)parameters["views"];
                Assert.Equal(300.0 / 304.8, converted[0]["x"]!.Value<double>(), 9);
                Assert.Equal(200.0 / 304.8, converted[0]["y"]!.Value<double>(), 9);
                Assert.Equal(1, converted[0]["view_id"]!.Value<int>());
                Assert.Equal(2, converted[1]["view_id"]!.Value<int>());
            }

            [Fact]
            public void DoubleArray_ConvertsEveryElement()
            {
                var parameters = new Dictionary<string, object> { ["boundary"] = new List<object> { 1000.0, 2000.0 } };
                var schemas = new[]
                {
                    new CommandParamSchema { Name = "boundary", Type = "double[]", UnitKind = UnitKinds.Length }
                };

                UnitsJson.ApplyToParameters(parameters, schemas, Canonical);

                var converted = (double[])parameters["boundary"];
                Assert.Equal(1000.0 / 304.8, converted[0], 9);
                Assert.Equal(2000.0 / 304.8, converted[1], 9);
            }

            [Fact]
            public void MissingParameterWithDefault_ConvertsTheDefault()
            {
                var parameters = new Dictionary<string, object>();
                var schemas = new[]
                {
                    new CommandParamSchema { Name = "height", Type = "double", UnitKind = UnitKinds.Length, Default = 3000 }
                };

                UnitsJson.ApplyToParameters(parameters, schemas, Canonical);

                Assert.Equal(3000.0 / 304.8, Convert.ToDouble(parameters["height"]), 9);
            }

            [Fact]
            public void MissingParameterWithoutDefault_StaysAbsent()
            {
                var parameters = new Dictionary<string, object>();
                var schemas = new[]
                {
                    new CommandParamSchema { Name = "height", Type = "double", UnitKind = UnitKinds.Length }
                };

                UnitsJson.ApplyToParameters(parameters, schemas, Canonical);

                Assert.False(parameters.ContainsKey("height"));
            }

            [Fact]
            public void NestedProperties_FromStringEncodedArray_Converts()
            {
                var parameters = new Dictionary<string, object>
                {
                    ["moves"] = "[{\"viewport_id\": 7, \"x\": 1000.0, \"y\": 2000.0}]"
                };
                var schemas = new[]
                {
                    new CommandParamSchema
                    {
                        Name = "moves", Type = "object", UnitKind = UnitKinds.Length,
                        Properties = new[]
                        {
                            new CommandParamSchema { Name = "viewport_id", Type = "int" },
                            new CommandParamSchema { Name = "x", Type = "double", UnitKind = UnitKinds.Length },
                            new CommandParamSchema { Name = "y", Type = "double", UnitKind = UnitKinds.Length }
                        }
                    }
                };

                UnitsJson.ApplyToParameters(parameters, schemas, Canonical);

                var converted = (JArray)parameters["moves"];
                Assert.Equal(1000.0 / 304.8, converted[0]["x"]!.Value<double>(), 9);
                Assert.Equal(2000.0 / 304.8, converted[0]["y"]!.Value<double>(), 9);
                Assert.Equal(7, converted[0]["viewport_id"]!.Value<int>());
            }

            [Fact]
            public void UnconvertibleScalar_PassesThroughUntouched()
            {
                var parameters = new Dictionary<string, object> { ["x"] = "not-a-number" };
                var schemas = new[]
                {
                    new CommandParamSchema { Name = "x", Type = "double", UnitKind = UnitKinds.Length }
                };

                UnitsJson.ApplyToParameters(parameters, schemas, Canonical);

                Assert.Equal("not-a-number", parameters["x"]);
            }

            [Fact]
            public void NumericStringScalar_Converts()
            {
                var parameters = new Dictionary<string, object> { ["x"] = "500" };
                var schemas = new[]
                {
                    new CommandParamSchema { Name = "x", Type = "double", UnitKind = UnitKinds.Length }
                };

                UnitsJson.ApplyToParameters(parameters, schemas, Canonical);

                Assert.Equal(500.0 / 304.8, Convert.ToDouble(parameters["x"]), 9);
            }
        }

        public class RewriteData
        {
            private static readonly IReadOnlyDictionary<string, string> Kinds =
                new Dictionary<string, string>
                {
                    ["diameter"] = UnitKinds.Length,
                    ["area"] = UnitKinds.Area
                };

            [Fact]
            public void DeclaredFields_ConvertToRequestUnits()
            {
                var data = JObject.Parse(@"{""diameter"": 0.328083989501312, ""count"": 3}");

                var rewritten = UnitsJson.RewriteData(data, Kinds, Canonical);

                Assert.Equal(100.0, (double)rewritten["diameter"]!, 6);
                Assert.Equal(3, (int)rewritten["count"]!);
            }

            [Fact]
            public void NestedObjectsAndArrays_Recurse()
            {
                var data = JObject.Parse(
                    @"{""pipes"":[{""diameter"": 0.328083989501312},{""diameter"": 0.164041994750656}], ""meta"": {""area"": 1.0}}");

                var rewritten = UnitsJson.RewriteData(data, Kinds, Canonical);

                var pipes = (JArray)rewritten["pipes"]!;
                var meta = (JObject)rewritten["meta"]!;
                Assert.Equal(100.0, (double)pipes[0]["diameter"]!, 6);
                Assert.Equal(50.0, (double)pipes[1]["diameter"]!, 6);
                Assert.Equal(0.093, (double)meta["area"]!, 3);
            }

            [Fact]
            public void EmptyMap_ReturnsInput()
            {
                var data = JObject.Parse(@"{""diameter"": 0.328}");

                var rewritten = UnitsJson.RewriteData(data, new Dictionary<string, string>(), Canonical);

                Assert.Same(data, rewritten);
            }
        }
    }
}
