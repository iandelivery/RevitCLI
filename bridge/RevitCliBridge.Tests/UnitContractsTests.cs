using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCliBridge;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for <see cref="UnitContracts"/>, <see cref="UnitsSelection"/>,
    /// and <see cref="UnitsRequestParser"/> — the units-contract vocabulary
    /// and request parsing.
    /// </summary>
    public class UnitContractsTests
    {
        public class Vocabulary
        {
            [Fact]
            public void Canonical_UsesMmM2M3Deg()
            {
                Assert.Equal("mm", UnitContracts.CanonicalUnits[UnitKinds.Length]);
                Assert.Equal("m2", UnitContracts.CanonicalUnits[UnitKinds.Area]);
                Assert.Equal("m3", UnitContracts.CanonicalUnits[UnitKinds.Volume]);
                Assert.Equal("deg", UnitContracts.CanonicalUnits[UnitKinds.Angle]);
            }

            [Fact]
            public void EveryCanonicalUnit_IsAllowedForItsKind()
            {
                foreach (var pair in UnitContracts.CanonicalUnits)
                    Assert.True(UnitContracts.IsAllowedUnit(pair.Key, pair.Value),
                        $"{pair.Value} must be allowed for {pair.Key}");
            }

            [Fact]
            public void UnknownKind_IsRejected()
            {
                Assert.False(UnitContracts.IsKnownKind("mass"));
                Assert.False(UnitContracts.IsAllowedUnit("mass", "kg"));
                Assert.Empty(UnitContracts.AllowedUnits("mass"));
            }

            [Fact]
            public void UnitNotAllowedForKind_IsRejected()
            {
                Assert.False(UnitContracts.IsAllowedUnit(UnitKinds.Length, "kg"));
                Assert.False(UnitContracts.IsAllowedUnit(UnitKinds.Angle, "mm"));
            }
        }

        public class Factors
        {
            [Fact]
            public void Length_FeetIsIdentityAndInchesAreTwelfths()
            {
                Assert.Equal(1.0, UnitContracts.ToInternal(UnitKinds.Length, "ft", 1.0), 12);
                Assert.Equal(1.0 / 12.0, UnitContracts.ToInternal(UnitKinds.Length, "in", 1.0), 12);
            }

            [Fact]
            public void Length_ThousandMmIsOneMeterExactly()
            {
                var mmAsFeet = UnitContracts.ToInternal(UnitKinds.Length, "mm", 1000.0);
                var mAsFeet = UnitContracts.ToInternal(UnitKinds.Length, "m", 1.0);
                Assert.Equal(mAsFeet, mmAsFeet, 9);
                Assert.Equal(1000.0, UnitContracts.FromInternal(UnitKinds.Length, "mm", mAsFeet), 6);
            }

            [Fact]
            public void Length_254MmIsTenInches()
            {
                var inches = UnitContracts.FromInternal(UnitKinds.Length, "in",
                    UnitContracts.ToInternal(UnitKinds.Length, "mm", 254.0));
                Assert.Equal(10.0, inches, 6);
            }

            [Fact]
            public void Angle_180DegIsPiRadians()
            {
                Assert.Equal(Math.PI, UnitContracts.ToInternal(UnitKinds.Angle, "deg", 180.0), 12);
                Assert.Equal(180.0, UnitContracts.FromInternal(UnitKinds.Angle, "deg", Math.PI), 6);
            }

            [Fact]
            public void Angle_RadIsIdentity()
            {
                Assert.Equal(1.5, UnitContracts.ToInternal(UnitKinds.Angle, "rad", 1.5), 12);
            }

            [Fact]
            public void Area_OneSquareMeterInSquareFeet()
            {
                Assert.Equal(1.0 / (0.3048 * 0.3048), UnitContracts.ToInternal(UnitKinds.Area, "m2", 1.0), 9);
            }

            [Fact]
            public void Volume_OneThousandLitersIsOneCubicMeter()
            {
                var liters = UnitContracts.ToInternal(UnitKinds.Volume, "l", 1000.0);
                var meters = UnitContracts.ToInternal(UnitKinds.Volume, "m3", 1.0);
                Assert.Equal(meters, liters, 9);
            }

            [Fact]
            public void DisallowedUnit_Throws()
            {
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => UnitContracts.ToInternal(UnitKinds.Length, "kg", 1.0));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => UnitContracts.FromInternal(UnitKinds.Length, "kg", 1.0));
            }
        }

        public class Rounding
        {
            [Fact]
            public void Output_RoundsToThreeDecimals()
            {
                Assert.Equal(832.5, UnitContracts.RoundOutput(832.4999999998686));
                Assert.Equal(1.234, UnitContracts.RoundOutput(1.2344999));
                Assert.Equal(-2.579, UnitContracts.RoundOutput(-2.5794999));
            }

            [Fact]
            public void FromInternal_Rounds_ToInternalDoesNot()
            {
                var mm = UnitContracts.FromInternal(UnitKinds.Length, "mm", 0.000123456789);
                Assert.Equal(0.038, mm, 9);

                var exact = UnitContracts.ToInternal(UnitKinds.Length, "mm", 831.9999999999);
                Assert.NotEqual(832.0, exact, 9);
            }

            private static int DecimalsOf(double value)
            {
                var text = value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                var dot = text.IndexOf('.');
                return dot < 0 ? 0 : text.Length - dot - 1;
            }
        }

        public class RequestParser
        {
            [Fact]
            public void Null_IsCanonical()
            {
                var ok = UnitsRequestParser.TryParse(null, out var selection, out var error);

                Assert.True(ok);
                Assert.Null(error);
                Assert.True(selection.IsCanonical);
            }

            [Fact]
            public void Map_ParsesAndValidates()
            {
                var raw = JObject.Parse(@"{""length"": ""ft"", ""area"": ""ft2""}");

                var ok = UnitsRequestParser.TryParse(raw, out var selection, out var error);

                Assert.True(ok);
                Assert.Null(error);
                Assert.False(selection.IsCanonical);
                Assert.Equal("ft", selection.Map[UnitKinds.Length]);
                Assert.Equal("ft2", selection.Map[UnitKinds.Area]);
            }

            [Fact]
            public void ProjectSentinel_SetsTheFlag()
            {
                var ok = UnitsRequestParser.TryParse("project", out var selection, out var error);

                Assert.True(ok);
                Assert.True(selection.UseProject);
                Assert.True(selection.Map.Count == 0);
            }

            [Fact]
            public void UnknownKind_FailsWithNamedError()
            {
                var raw = JObject.Parse(@"{""mass"": ""kg""}");

                var ok = UnitsRequestParser.TryParse(raw, out _, out var error);

                Assert.False(ok);
                Assert.Equal("Unknown unit kind 'mass' in 'units'. Allowed kinds: length, area, volume, angle.", error);
            }

            [Fact]
            public void UnitWrongForKind_FailsWithNamedError()
            {
                var raw = JObject.Parse(@"{""angle"": ""mm""}");

                var ok = UnitsRequestParser.TryParse(raw, out _, out var error);

                Assert.False(ok);
                Assert.Equal("Unit 'mm' is not allowed for kind 'angle'. Allowed units: deg, rad.", error);
            }

            [Fact]
            public void OtherString_FailsWithNamedError()
            {
                var ok = UnitsRequestParser.TryParse("metric", out _, out var error);

                Assert.False(ok);
                Assert.Equal("'units' must be an object of kind-to-unit pairs or the string \"project\".", error);
            }

            [Fact]
            public void EmptyMap_IsCanonical()
            {
                var raw = new JObject();

                var ok = UnitsRequestParser.TryParse(raw, out var selection, out var error);

                Assert.True(ok);
                Assert.True(selection.IsCanonical);
            }
        }
    }
}
