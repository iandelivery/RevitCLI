using System;

namespace RevitCliBridge.Handlers
{
    /// <summary>
    /// Pure parameter conversion shared by handlers that read the raw
    /// parameter dictionary instead of using the typed binder. Linked into
    /// the test project per the repo's Revit-free test convention.
    /// </summary>
    public static class ParamConverter
    {
        /// <summary>
        /// Converts a raw parameter value to bool. Accepts bool and the
        /// strings "true"/"false" (case-insensitive). Returns false for any
        /// other shape so the caller can answer with a named error.
        /// </summary>
        public static bool TryConvertBool(object? value, out bool result)
        {
            switch (value)
            {
                case null:
                    result = false;
                    return false;
                case bool b:
                    result = b;
                    return true;
                case string s:
                    if (bool.TryParse(s, out var parsed))
                    {
                        result = parsed;
                        return true;
                    }
                    result = false;
                    return false;
                default:
                    try
                    {
                        result = Convert.ToBoolean(value);
                        return true;
                    }
                    catch
                    {
                        result = false;
                        return false;
                    }
            }
        }
    }
}
