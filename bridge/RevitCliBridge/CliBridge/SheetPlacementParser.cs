using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;

namespace RevitCliBridge
{
    /// <summary>
    /// One entry of a `views` or `moves` array: an element ID plus optional
    /// sheet coordinates in millimeters.
    /// </summary>
    public sealed class PlacementItem
    {
        /// <summary>The parsed element ID.</summary>
        public long Id { get; }

        /// <summary>X in millimeters, or null when the entry omits it.</summary>
        public double? X { get; }

        /// <summary>Y in millimeters, or null when the entry omits it.</summary>
        public double? Y { get; }

        public PlacementItem(long id, double? x, double? y)
        {
            Id = id;
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Parses the {id, x, y} object arrays that the sheet viewport commands
    /// receive as raw Newtonsoft tokens. Pure logic, so the test project
    /// links this file directly.
    /// </summary>
    public static class SheetPlacementParser
    {
        /// <summary>
        /// Parses an array of {id, x, y} objects. Fails with a named error
        /// when the value is not an array, is empty, holds a non-object item,
        /// lacks the ID, or carries coordinates that are missing, half-set,
        /// or not numeric.
        /// </summary>
        /// <param name="raw">The parameter value, typically a JArray.</param>
        /// <param name="arrayName">Parameter name for error messages.</param>
        /// <param name="idName">ID field name, "view_id" or "viewport_id".</param>
        /// <param name="requireCoordinates">True when x and y must be present.</param>
        public static bool TryParse(
            object? raw,
            string arrayName,
            string idName,
            bool requireCoordinates,
            out List<PlacementItem> items,
            out string? error)
        {
            items = new List<PlacementItem>();
            error = null;

            IEnumerable? sequence = raw switch
            {
                JArray array => array,
                List<object> list => list,
                object[] elements => elements,
                _ => null
            };

            if (sequence is null)
            {
                error = $"'{arrayName}' must be an array of objects.";
                return false;
            }

            var index = 0;
            foreach (var entry in sequence)
            {
                var obj = ToJObject(entry);
                if (obj is null)
                {
                    error = $"'{arrayName}' item {index} must be an object.";
                    return false;
                }

                if (!TryReadInt64(obj, idName, out var id))
                {
                    error = $"'{arrayName}' item {index} needs an integer '{idName}'.";
                    return false;
                }

                var xOk = TryReadDouble(obj, "x", out var x);
                var yOk = TryReadDouble(obj, "y", out var y);
                if (!xOk || !yOk)
                {
                    error = $"'{arrayName}' item {index} needs numeric 'x' and 'y'.";
                    return false;
                }

                if ((x is null) != (y is null))
                {
                    error = $"'{arrayName}' item {index} must set both 'x' and 'y'.";
                    return false;
                }

                if (requireCoordinates && (x is null || y is null))
                {
                    error = $"'{arrayName}' item {index} is missing 'x' or 'y'.";
                    return false;
                }

                items.Add(new PlacementItem(id, x, y));
                index++;
            }

            if (items.Count == 0)
            {
                error = $"'{arrayName}' must contain at least one item.";
                return false;
            }

            return true;
        }

        private static JObject? ToJObject(object? entry)
        {
            return entry switch
            {
                JObject obj => obj,
                Dictionary<string, object> dict => JObject.FromObject(dict),
                _ => null
            };
        }

        private static bool TryReadInt64(JObject obj, string key, out long value)
        {
            value = 0;
            var token = obj[key];
            if (token is null || token.Type == JTokenType.Null)
                return false;

            try
            {
                value = Convert.ToInt64(Unwrap(token));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadDouble(JObject obj, string key, out double? value)
        {
            value = null;
            var token = obj[key];
            if (token is null || token.Type == JTokenType.Null)
                return true;

            try
            {
                value = Convert.ToDouble(Unwrap(token));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object Unwrap(JToken token)
        {
            return token is JValue value && value.Value is not null
                ? value.Value
                : token.ToString();
        }
    }
}
