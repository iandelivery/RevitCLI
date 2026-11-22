using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge
{
    /// <summary>
    /// The Revit-facing half of the units middleware: parses the request's
    /// units selection, resolves the effective unit per kind (explicit map,
    /// then the active document's decimal display units for the "project"
    /// sentinel, then canonical), converts declared-kind parameters to
    /// internal units before the handler runs, and stamps the units echo
    /// plus the field rewrites onto the response JSON. One place converts;
    /// handlers declare kinds and stay unit-blind. No shared state: every
    /// request resolves against its own document snapshot.
    /// </summary>
    internal static class UnitsContext
    {
        internal sealed class Prepared
        {
            /// <summary>Named error response when the units field is invalid; execution stops.</summary>
            public string? ErrorJson;

            /// <summary>The validated selection; null when the field was absent.</summary>
            public UnitsSelection? Selection;

            /// <summary>Effective unit per known kind.</summary>
            public IReadOnlyDictionary<string, string> Effective = CanonicalMap();

            public bool AnyConversions => Selection is not null && !Selection.IsCanonical;
        }

        internal static Prepared Prepare(UIApplication app, QueuedCommand cmd)
        {
            var prepared = new Prepared();
            var raw = cmd.Units;

            if (raw is null)
                return prepared;

            if (!UnitsRequestParser.TryParse(raw, out var selection, out var error))
            {
                prepared.ErrorJson = CommandResponse.Error(cmd.TaskId, error!).ToJson();
                return prepared;
            }

            prepared.Selection = selection;

            Document? doc = null;
            if (selection.UseProject)
            {
                doc = app.ActiveUIDocument?.Document;
                if (doc is null)
                {
                    prepared.ErrorJson = CommandResponse.Error(cmd.TaskId,
                        "units \"project\" requires an active document.").ToJson();
                    return prepared;
                }
            }

            prepared.Effective = ResolveEffective(selection, doc);
            cmd.ResolvedUnits = prepared.Effective;
            return prepared;
        }

        /// <summary>
        /// Converts the declared-kind parameters of the command in place.
        /// No-op until a handler declares unit_kind on its schema.
        /// </summary>
        internal static void ApplyInputs(
            QueuedCommand cmd, Prepared prepared, IEnumerable<CommandParamSchema> schemas)
        {
            if (cmd.Parameters is not Dictionary<string, object> parameters)
                return;

            UnitsJson.ApplyToParameters(parameters, schemas, kind => UnitFor(prepared, kind));
        }

        /// <summary>
        /// Stamps the units echo and applies the handler's response field
        /// rewrites onto the serialized response.
        /// </summary>
        internal static string Finish(
            Handlers.BridgeCommandBase handler, Prepared prepared, string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                var data = obj["data"];

                if (data is not null && handler.ResponseFieldKinds.Count > 0)
                {
                    data.Replace(UnitsJson.RewriteData(data, handler.ResponseFieldKinds,
                        kind => UnitFor(prepared, kind)));
                }

                var echo = new JObject();
                foreach (var pair in prepared.Effective)
                    echo[pair.Key] = pair.Value;
                obj["units"] = echo;

                return obj.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                return json;
            }
        }

        private static string UnitFor(Prepared prepared, string kind)
        {
            return prepared.Effective.TryGetValue(kind, out var unit)
                ? unit
                : UnitContracts.CanonicalUnits[kind];
        }

        internal static IReadOnlyDictionary<string, string> ResolveEffective(UnitsSelection? selection, Document? projectDoc)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in UnitContracts.CanonicalUnits)
                map[pair.Key] = pair.Value;

            if (selection is null)
                return map;

            foreach (var pair in selection.Map)
                map[pair.Key] = pair.Value;

            if (selection.UseProject && projectDoc is not null)
            {
                foreach (var pair in ReadProjectUnits(projectDoc))
                    if (map.ContainsKey(pair.Key))
                        map[pair.Key] = pair.Value;
            }

            return map;
        }

        internal static Dictionary<string, string> CanonicalMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in UnitContracts.CanonicalUnits)
                map[pair.Key] = pair.Value;
            return map;
        }

#if (R19 || R20)
        private static IReadOnlyDictionary<string, string> ReadProjectUnits(Document doc)
        {
            var units = doc.GetUnits();
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [UnitKinds.Length] = Map(units.GetFormatOptions(UnitType.UT_Length).DisplayUnits, "mm"),
                [UnitKinds.Area] = Map(units.GetFormatOptions(UnitType.UT_Area).DisplayUnits, "m2"),
                [UnitKinds.Volume] = Map(units.GetFormatOptions(UnitType.UT_Volume).DisplayUnits, "m3"),
                [UnitKinds.Angle] = Map(units.GetFormatOptions(UnitType.UT_Angle).DisplayUnits, "deg")
            };
        }

        private static string Map(DisplayUnitType display, string canonical)
        {
            return display switch
            {
                DisplayUnitType.DUT_MILLIMETERS => "mm",
                DisplayUnitType.DUT_CENTIMETERS => "cm",
                DisplayUnitType.DUT_METERS => "m",
                DisplayUnitType.DUT_DECIMAL_FEET => "ft",
                DisplayUnitType.DUT_FEET_FRACTIONAL_INCHES => "ft",
                DisplayUnitType.DUT_DECIMAL_INCHES => "in",
                DisplayUnitType.DUT_FRACTIONAL_INCHES => "in",
                DisplayUnitType.DUT_SQUARE_METERS => "m2",
                DisplayUnitType.DUT_SQUARE_MILLIMETERS => "mm2",
                DisplayUnitType.DUT_SQUARE_FEET => "ft2",
                DisplayUnitType.DUT_CUBIC_METERS => "m3",
                DisplayUnitType.DUT_CUBIC_CENTIMETERS => "mm3",
                DisplayUnitType.DUT_CUBIC_FEET => "ft3",
                DisplayUnitType.DUT_CUBIC_INCHES => "l",
                DisplayUnitType.DUT_DECIMAL_DEGREES => "deg",
                DisplayUnitType.DUT_RADIANS => "rad",
                _ => canonical
            };
        }
#else
        private static IReadOnlyDictionary<string, string> ReadProjectUnits(Document doc)
        {
            var units = doc.GetUnits();
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [UnitKinds.Length] = Map(units.GetFormatOptions(SpecTypeId.Length).GetUnitTypeId(), "mm"),
                [UnitKinds.Area] = Map(units.GetFormatOptions(SpecTypeId.Area).GetUnitTypeId(), "m2"),
                [UnitKinds.Volume] = Map(units.GetFormatOptions(SpecTypeId.Volume).GetUnitTypeId(), "m3"),
                [UnitKinds.Angle] = Map(units.GetFormatOptions(SpecTypeId.Angle).GetUnitTypeId(), "deg")
            };
        }

        private static string Map(ForgeTypeId unit, string canonical)
        {
            if (unit == UnitTypeId.Millimeters) return "mm";
            if (unit == UnitTypeId.Centimeters) return "cm";
            if (unit == UnitTypeId.Meters) return "m";
            if (unit == UnitTypeId.Feet) return "ft";
            if (unit == UnitTypeId.FractionalFeet) return "ft";
            if (unit == UnitTypeId.Inches) return "in";
            if (unit == UnitTypeId.FractionalInches) return "in";
            if (unit == UnitTypeId.SquareMeters) return "m2";
            if (unit == UnitTypeId.SquareMillimeters) return "mm2";
            if (unit == UnitTypeId.SquareFeet) return "ft2";
            if (unit == UnitTypeId.CubicMeters) return "m3";
            if (unit == UnitTypeId.CubicCentimeters) return "mm3";
            if (unit == UnitTypeId.CubicFeet) return "ft3";
            if (unit == UnitTypeId.CubicInches) return "l";
            if (unit == UnitTypeId.Degrees) return "deg";
            if (unit == UnitTypeId.Radians) return "rad";
            return canonical;
        }
#endif
    }
}
