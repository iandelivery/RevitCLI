using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace RevitCliBridge
{
    /// <summary>
    /// The unit kinds of the units contract. A kind names what a number
    /// measures; the request chooses the unit for each kind.
    /// </summary>
    public static class UnitKinds
    {
        /// <summary>Distances, coordinates, and sizes. Internal unit: feet.</summary>
        public const string Length = "length";

        /// <summary>Surface measures. Internal unit: square feet.</summary>
        public const string Area = "area";

        /// <summary>Capacity measures. Internal unit: cubic feet.</summary>
        public const string Volume = "volume";

        /// <summary>Rotation measures. Internal unit: radians.</summary>
        public const string Angle = "angle";

        /// <summary>A number the contract passes through unchanged.</summary>
        public const string Raw = "raw";
    }

    /// <summary>
    /// The units-contract vocabulary: the canonical unit per kind, the units
    /// allowed per kind, and the factor from each unit to the Revit internal
    /// unit of its kind. The catalog is code on purpose: a factor is a
    /// definition (one foot is 304.8 millimeters), so it is versioned and
    /// unit-tested, never configured. Requests choose units; they cannot
    /// invent them. Revit-free, so the test project links this file directly.
    /// </summary>
    public static class UnitContracts
    {
        /// <summary>Output precision in decimals; inputs are never rounded.</summary>
        public const int OutputDecimals = 3;

        private const double FeetPerMeter = 1.0 / 0.3048;
        private const double SquareFeetPerSquareMeter = FeetPerMeter * FeetPerMeter;
        private const double CubicFeetPerCubicMeter = FeetPerMeter * FeetPerMeter * FeetPerMeter;
        private const double CubicFeetPerLiter = 0.001 * CubicFeetPerCubicMeter;

        private static readonly Dictionary<string, string> Canonical =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [UnitKinds.Length] = "mm",
                [UnitKinds.Area] = "m2",
                [UnitKinds.Volume] = "m3",
                [UnitKinds.Angle] = "deg"
            };

        private static readonly Dictionary<string, Dictionary<string, double>> UnitsByKind =
            new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal)
            {
                [UnitKinds.Length] = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["mm"] = 1.0 / 304.8,
                    ["cm"] = 1.0 / 30.48,
                    ["m"] = FeetPerMeter,
                    ["ft"] = 1.0,
                    ["in"] = 1.0 / 12.0
                },
                [UnitKinds.Area] = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["m2"] = SquareFeetPerSquareMeter,
                    ["mm2"] = 1.0 / (304.8 * 304.8),
                    ["ft2"] = 1.0
                },
                [UnitKinds.Volume] = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["m3"] = CubicFeetPerCubicMeter,
                    ["mm3"] = 1.0 / (304.8 * 304.8 * 304.8),
                    ["ft3"] = 1.0,
                    ["l"] = CubicFeetPerLiter
                },
                [UnitKinds.Angle] = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["deg"] = Math.PI / 180.0,
                    ["rad"] = 1.0
                }
            };

        /// <summary>The canonical unit ids, one per known kind.</summary>
        public static IReadOnlyDictionary<string, string> CanonicalUnits => Canonical;

        /// <summary>True when the kind has a conversion table.</summary>
        public static bool IsKnownKind(string kind)
        {
            return UnitsByKind.ContainsKey(kind);
        }

        /// <summary>The units allowed for a kind, or an empty list.</summary>
        public static IReadOnlyList<string> AllowedUnits(string kind)
        {
            return UnitsByKind.TryGetValue(kind, out var units)
                ? new List<string>(units.Keys)
                : new List<string>();
        }

        /// <summary>True when the unit id is allowed for the kind.</summary>
        public static bool IsAllowedUnit(string kind, string unit)
        {
            return UnitsByKind.TryGetValue(kind, out var units)
                && units.ContainsKey(unit);
        }

        /// <summary>
        /// Converts a value from the chosen unit to the internal unit of its
        /// kind. Inputs only: the result is never rounded.
        /// </summary>
        public static double ToInternal(string kind, string unit, double value)
        {
            if (!UnitsByKind.TryGetValue(kind, out var units) || !units.TryGetValue(unit, out var factor))
                throw new ArgumentOutOfRangeException(nameof(unit), $"Unit '{unit}' is not allowed for kind '{kind}'.");

            return value * factor;
        }

        /// <summary>
        /// Converts a value from the internal unit of its kind to the chosen
        /// unit, rounded to <see cref="OutputDecimals"/> to remove
        /// conversion noise.
        /// </summary>
        public static double FromInternal(string kind, string unit, double value)
        {
            if (!UnitsByKind.TryGetValue(kind, out var units) || !units.TryGetValue(unit, out var factor) || factor == 0)
                throw new ArgumentOutOfRangeException(nameof(unit), $"Unit '{unit}' is not allowed for kind '{kind}'.");

            return RoundOutput(value / factor);
        }

        /// <summary>Rounds an output value to the contract precision.</summary>
        public static double RoundOutput(double value)
        {
            return Math.Round(value, OutputDecimals, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>
    /// The parsed form of a request's `units` field: either a kind-to-unit
    /// map, or the sentinel that resolves per kind against the active
    /// document's decimal display units.
    /// </summary>
    public sealed class UnitsSelection
    {
        /// <summary>Kind to unit id. Empty when the request only sets the sentinel.</summary>
        public IReadOnlyDictionary<string, string> Map { get; }

        /// <summary>True when the document's display units fill every kind the map omits.</summary>
        public bool UseProject { get; }

        public UnitsSelection(IReadOnlyDictionary<string, string> map, bool useProject)
        {
            Map = map;
            UseProject = useProject;
        }

        /// <summary>
        /// True when the selection carries no map and no sentinel, so the
        /// canonical units apply.
        /// </summary>
        public bool IsCanonical => Map.Count == 0 && !UseProject;
    }

    /// <summary>
    /// Parses the raw `units` value of a request: a JSON object of
    /// kind-to-unit pairs, the string "project", or null for canonical.
    /// Pure logic, linked into the test project.
    /// </summary>
    public static class UnitsRequestParser
    {
        /// <summary>The sentinel string that resolves against the document.</summary>
        public const string ProjectSentinel = "project";

        /// <summary>
        /// Parses and validates the raw value. Every map entry must name a
        /// known kind and an allowed unit, so a typo fails the request
        /// instead of silently producing wrong numbers.
        /// </summary>
        public static bool TryParse(object? raw, out UnitsSelection selection, out string? error)
        {
            selection = new UnitsSelection(new Dictionary<string, string>(), false);
            error = null;

            if (raw is null)
                return true;

            if (raw is string sentry)
            {
                if (!string.Equals(sentry, ProjectSentinel, StringComparison.Ordinal))
                {
                    error = $"'units' must be an object of kind-to-unit pairs or the string \"{ProjectSentinel}\".";
                    return false;
                }

                selection = new UnitsSelection(new Dictionary<string, string>(), true);
                return true;
            }

            var obj = raw as JObject;
            if (obj is null)
            {
                error = "'units' must be an object of kind-to-unit pairs or the string \"project\".";
                return false;
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in obj.Properties())
            {
                var kind = prop.Name;
                var unit = prop.Value?.ToString();

                if (!UnitContracts.IsKnownKind(kind))
                {
                    error = $"Unknown unit kind '{kind}' in 'units'. Allowed kinds: {string.Join(", ", UnitContracts.CanonicalUnits.Keys)}.";
                    return false;
                }

                if (unit is null || !UnitContracts.IsAllowedUnit(kind, unit))
                {
                    error = $"Unit '{unit}' is not allowed for kind '{kind}'. Allowed units: {string.Join(", ", UnitContracts.AllowedUnits(kind))}.";
                    return false;
                }

                map[kind] = unit;
            }

            selection = new UnitsSelection(map, false);
            return true;
        }

        /// <summary>
        /// Formats a double the way the contract serializes numbers.
        /// Reserved for response writers.
        /// </summary>
        public static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
