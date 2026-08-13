using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Reflective parameter binder — populates a POCO from a parameters
    /// dictionary using <see cref="ParamAttribute"/> annotations.
    /// </summary>
    /// <remarks>
    /// This eliminates the boilerplate of N×<c>HandlerUtilities.GetXxxOrNull</c>
    /// calls plus manual null-checks plus manual error responses that every
    /// handler previously repeated. Handler authors declare a POCO with
    /// <c>[Param("name", Required = true)]</c> and call
    /// <c>ParameterBinder.Bind&lt;T&gt;(parameters)</c> — missing required
    /// params raise <see cref="MissingParameterException"/>, type mismatches
    /// raise <see cref="ParameterTypeException"/>, both of which
    /// <see cref="BridgeCommandBase"/> converts to a 400-style error response.
    ///
    /// Supported property types: <c>int</c>, <c>int?</c>, <c>double</c>,
    /// <c>double?</c>, <c>string</c>, <c>int[]</c>, <c>bool</c>, <c>bool?</c>.
    /// Nullable value types are treated as optional (the property keeps its
    /// default when the parameter is absent).
    /// </remarks>
    public static class ParameterBinder
    {
        /// <summary>
        /// Build a <typeparamref name="T"/> from <paramref name="parameters"/>.
        /// </summary>
        /// <exception cref="MissingParameterException">A required parameter
        /// (no default, non-nullable, absent from dictionary) is missing.</exception>
        /// <exception cref="ParameterTypeException">A value cannot be converted
        /// to its target property type.</exception>
        public static T Bind<T>(IDictionary<string, object>? parameters) where T : new()
        {
            var result = new T();
            var type = typeof(T);

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<ParamAttribute>();
                if (attr is null) continue;

                var name = string.IsNullOrEmpty(attr.Name) ? ToSnakeCase(prop.Name) : attr.Name;
                object? raw = null;
                var hasValue = parameters is not null && parameters.TryGetValue(name, out raw) && raw is not null;
                var isNullable = IsNullable(prop.PropertyType);

                // Resolve the effective value: explicit > default > (none).
                object? value = hasValue ? raw : attr.Default;

                if (value is null)
                {
                    if (attr.Required && !isNullable)
                        throw new MissingParameterException(name);
                    // Leave property at its default (null for nullable, 0/false for value types).
                    continue;
                }

                object? converted = ConvertValue(value, prop.PropertyType, name);
                prop.SetValue(result, converted);
            }

            return result;
        }

        /// <summary>
        /// Same as <see cref="Bind{T}(IDictionary{string, object}?)"/> but
        /// accepts the loose <c>object?</c> parameter bag from
        /// <see cref="QueuedCommand.Parameters"/> (typically a
        /// <c>Dictionary&lt;string, object&gt;</c> but typed as object).
        /// </summary>
        public static T Bind<T>(object? parameters) where T : new()
        {
            var dict = parameters as IDictionary<string, object>;
            if (dict is null && parameters is IDictionary loose)
            {
                dict = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry e in loose)
                    dict[e.Key?.ToString() ?? string.Empty] = e.Value!;
            }
            return Bind<T>(dict);
        }

        /// <summary>
        /// Convert a raw parameter value (typically from JSON deserialization,
        /// so <c>long</c>/<c>double</c>/<c>string</c>/<c>JArray</c>) to the
        /// target property type.
        /// </summary>
        private static object? ConvertValue(object value, Type targetType, string paramName)
        {
            var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (nonNullable == typeof(string))
                return value.ToString();

            if (nonNullable == typeof(int))
            {
                try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
                catch (Exception ex) { throw new ParameterTypeException(paramName, targetType, ex); }
            }

            if (nonNullable == typeof(double))
            {
                try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
                catch (Exception ex) { throw new ParameterTypeException(paramName, targetType, ex); }
            }

            if (nonNullable == typeof(bool))
            {
                try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
                catch (Exception ex) { throw new ParameterTypeException(paramName, targetType, ex); }
            }

            if (nonNullable == typeof(int[]))
            {
                try
                {
                    if (value is IEnumerable enumerable and not string)
                    {
                        var list = new List<int>();
                        foreach (var item in enumerable)
                            list.Add(Convert.ToInt32(item, CultureInfo.InvariantCulture));
                        return list.ToArray();
                    }
                    throw new FormatException("Expected an array of integers.");
                }
                catch (Exception ex)
                {
                    throw new ParameterTypeException(paramName, targetType, ex);
                }
            }

            // Fallback: try direct assignment for matching types.
            if (nonNullable.IsAssignableFrom(value.GetType()))
                return value;

            throw new ParameterTypeException(paramName, targetType,
                new InvalidOperationException($"No conversion from {value.GetType().Name} to {targetType.Name}."));
        }

        private static bool IsNullable(Type type)
            => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

        private static string ToSnakeCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (i > 0 && char.IsUpper(c))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
