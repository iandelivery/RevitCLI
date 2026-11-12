namespace RevitCliBridge.Handlers
{
    /// <summary>
    /// Pure comparison logic for search_elements, split from the handler so
    /// the test project can cover it without the Revit API. The handler
    /// reads the parameter twice, as text (AsString/AsValueString) and as a
    /// number (AsInteger/AsDouble/AsElementId), then delegates here.
    /// </summary>
    public static class ParamMatcher
    {
        /// <summary>
        /// Equality against a caller-supplied string. Numeric parameters
        /// match numerically when the target parses as a number, so that
        /// "1" matches an integer 1 and a double 1.0; otherwise they fall
        /// back to ordinal-ignore-case text equality.
        /// </summary>
        public static bool EqualsTarget(string? textValue, double? numericValue, string? target)
        {
            if (numericValue.HasValue && double.TryParse(target, out var parsed))
                return numericValue.Value.Equals(parsed);

            return string.Equals(textValue, target, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Three-way comparison for gt/lt. Numeric parameters compare
        /// numerically when the target parses as a number; everything else
        /// compares as ordinal-ignore-case text. Text-vs-null sorts before
        /// any text, mirroring string.Compare semantics.
        /// </summary>
        public static int Compare(string? textValue, double? numericValue, string? target)
        {
            if (numericValue.HasValue && double.TryParse(target, out var parsed))
                return numericValue.Value.CompareTo(parsed);

            return string.Compare(textValue, target, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
