using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge
{
    /// <summary>
    /// The JSON-facing half of the units middleware: converts declared-kind
    /// request parameters to internal units, and rewrites declared response
    /// fields from internal units to the request units. Pure logic on
    /// Newtonsoft tokens and schema objects, so the test project links this
    /// file directly. The Revit-facing half, which resolves the effective
    /// unit per kind, lives in UnitsContext.
    /// </summary>
    public static class UnitsJson
    {
        /// <summary>
        /// Converts every schema-declared dimensional parameter in place,
        /// from the request unit to the internal unit. The resolver names
        /// the effective unit for a kind. Flat doubles, double arrays, and
        /// object-array items declared through nested Properties convert;
        /// everything else is left untouched.
        /// </summary>
        public static void ApplyToParameters(
            Dictionary<string, object> parameters,
            IEnumerable<CommandParamSchema> schemas,
            Func<string, string> unitForKind)
        {
            foreach (var schema in schemas)
            {
                if (string.IsNullOrEmpty(schema.UnitKind) || schema.UnitKind == UnitKinds.Raw)
                    continue;
                if (!parameters.TryGetValue(schema.Name, out var value) || value is null)
                {
                    if (schema.Default is not null)
                        parameters[schema.Name] = ToInternal(schema.Default, schema.UnitKind!, unitForKind);
                    continue;
                }

                parameters[schema.Name] = ConvertValue(value, schema, unitForKind);
            }
        }

        private static object ConvertValue(
            object value, CommandParamSchema schema, Func<string, string> unitForKind)
        {
            if (schema.Properties is { Length: > 0 } && value is IEnumerable items && value is not string)
            {
                var converted = new JArray();
                foreach (var item in items)
                {
                    if (item is JObject obj)
                    {
                        foreach (var sub in schema.Properties)
                        {
                            var subKind = sub.UnitKind;
                            if (string.IsNullOrEmpty(subKind) || subKind == UnitKinds.Raw)
                                continue;
                            if (obj[sub.Name] is { } token && token.Type != JTokenType.Null)
                                obj[sub.Name] = new JValue(ToInternal(token, subKind!, unitForKind));
                        }
                        converted.Add(obj);
                    }
                    else
                    {
                        converted.Add(item);
                    }
                }
                return converted;
            }

            if (value is IEnumerable array && value is not string)
            {
                var list = new List<double>();
                foreach (var item in array)
                    list.Add(ToInternal(item, schema.UnitKind!, unitForKind));
                return list.ToArray();
            }

            return ToInternal(value, schema.UnitKind!, unitForKind);
        }

        private static double ToInternal(object token, string kind, Func<string, string> unitForKind)
        {
            var value = Convert.ToDouble(token is JValue jv && jv.Value is not null ? jv.Value : token);
            return UnitContracts.ToInternal(kind, unitForKind(kind), value);
        }

        /// <summary>
        /// Rewrites the data subtree of a response: every field named in
        /// fieldKinds that carries a numeric value converts from the Revit
        /// internal unit to the request unit, rounded to contract precision.
        /// Recurses through nested objects and arrays. Returns the rewritten
        /// token, or the input when nothing matched.
        /// </summary>
        public static JToken RewriteData(
            JToken data,
            IReadOnlyDictionary<string, string> fieldKinds,
            Func<string, string> unitForKind)
        {
            if (fieldKinds.Count == 0)
                return data;

            switch (data)
            {
                case JObject obj:
                    foreach (var prop in obj.Properties())
                    {
                        if (prop.Value.Type == JTokenType.Float || prop.Value.Type == JTokenType.Integer)
                        {
                            if (fieldKinds.TryGetValue(prop.Name, out var kind)
                                && kind != UnitKinds.Raw)
                            {
                                var value = ((JValue)prop.Value).Value is double d ? d : Convert.ToDouble(((JValue)prop.Value).Value);
                                prop.Value = new JValue(UnitContracts.FromInternal(kind, unitForKind(kind), value));
                                continue;
                            }
                        }
                        prop.Value = RewriteData(prop.Value, fieldKinds, unitForKind);
                    }
                    return obj;
                case JArray array:
                    for (var i = 0; i < array.Count; i++)
                        array[i] = RewriteData(array[i], fieldKinds, unitForKind);
                    return array;
                default:
                    return data;
            }
        }
    }
}
