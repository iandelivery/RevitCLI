using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitCliBridge.Handlers.Parameters
{
    /// <summary>
    /// Converts parameter values between the units contract and Revit
    /// internal units by looking at the parameter's own spec type, not the
    /// schema: the target spec is a property of the element's parameter, so
    /// the four geometric kinds convert (length to the length unit, area to
    /// the area unit, and so on) and everything else passes through as raw.
    /// The version difference, UnitType on 2019 and 2020 versus SpecTypeId
    /// on 2021 and 2022, stays inside this class. Pure static facade with no
    /// state, per the repo convention of facades over base classes.
    /// </summary>
    internal static class ParameterUnits
    {
        /// <summary>
        /// The units-contract kind of a parameter, or "raw" when the spec is
        /// not one of the converted geometric kinds.
        /// </summary>
        internal static string KindFor(Parameter p)
        {
#if (R19 || R20)
            switch (p.Definition.UnitType)
            {
                case UnitType.UT_Length:
                case UnitType.UT_SheetLength:
                    return UnitKinds.Length;
                case UnitType.UT_Area:
                    return UnitKinds.Area;
                case UnitType.UT_Volume:
                    return UnitKinds.Volume;
                case UnitType.UT_Angle:
                    return UnitKinds.Angle;
                default:
                    return UnitKinds.Raw;
            }
#else
#if R22
            var spec = p.Definition.GetDataType();
#else
            var spec = p.Definition.GetSpecTypeId();
#endif
            if (spec == SpecTypeId.Length || spec == SpecTypeId.SheetLength)
                return UnitKinds.Length;
            if (spec == SpecTypeId.Area)
                return UnitKinds.Area;
            if (spec == SpecTypeId.Volume)
                return UnitKinds.Volume;
            if (spec == SpecTypeId.Angle)
                return UnitKinds.Angle;
            return UnitKinds.Raw;
#endif
        }

        /// <summary>
        /// The effective request unit for a kind, from the units the
        /// middleware resolved onto the command, falling back to canonical.
        /// </summary>
        internal static string UnitFor(IReadOnlyDictionary<string, string>? resolved, string kind)
        {
            if (resolved is not null && resolved.TryGetValue(kind, out var unit) && unit.Length > 0)
                return unit;
            return UnitContracts.CanonicalUnits[kind];
        }

        /// <summary>
        /// Reads a double parameter in the request units. Raw kinds and
        /// non-double storage types return the plain value unchanged.
        /// </summary>
        internal static object? ToCliValue(Parameter p, IReadOnlyDictionary<string, string>? resolved)
        {
            if (p.StorageType != StorageType.Double)
                return p.StorageType switch
                {
                    StorageType.Integer => p.AsInteger(),
                    StorageType.String => p.AsString(),
                    StorageType.ElementId => p.AsElementId().IntegerValue,
                    _ => null
                };

            var raw = p.AsDouble();
            var kind = KindFor(p);
            return kind == UnitKinds.Raw
                ? raw
                : UnitContracts.FromInternal(kind, UnitFor(resolved, kind), raw);
        }

        /// <summary>
        /// Converts an incoming double from the request units to internal
        /// units for writing. Raw kinds pass through unchanged, so ratios and
        /// coefficients stay exact. Returns false when the value is not
        /// numeric.
        /// </summary>
        internal static bool TryToInternalDouble(
            Parameter p, IReadOnlyDictionary<string, string>? resolved, object value, out double internalValue)
        {
            internalValue = 0;
            if (!double.TryParse(value.ToString(), out var parsed))
                return false;

            var kind = KindFor(p);
            internalValue = kind == UnitKinds.Raw
                ? parsed
                : UnitContracts.ToInternal(kind, UnitFor(resolved, kind), parsed);
            return true;
        }
    }
}
