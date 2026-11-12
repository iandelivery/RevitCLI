namespace RevitCliBridge.Handlers
{
    /// <summary>
    /// Which RevitLinkInstance a linked-document query targets: by instance
    /// element id or by exact instance name.
    /// </summary>
    public class LinkSelector
    {
        public int? InstanceId { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>
    /// Pure link-selector parsing, split from HandlerUtilities so the test
    /// project can link this file without pulling in Revit API types.
    /// HandlerUtilities.ResolveLinkSelector delegates here.
    /// </summary>
    public static class LinkSelectorParser
    {
        /// <summary>
        /// Parses a raw `link` parameter value into an instance-id selector
        /// or an exact-name selector. Null/blank yields null; a positive
        /// integer yields an id selector; anything else is a name (trimmed).
        /// </summary>
        public static LinkSelector? ResolveLinkSelector(string? raw)
        {
            if (raw is null)
                return null;

            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
                return null;

            if (int.TryParse(trimmed, out int id) && id > 0)
                return new LinkSelector { InstanceId = id };

            return new LinkSelector { Name = trimmed };
        }

        /// <summary>
        /// Human-readable form of a selector for error messages, e.g.
        /// "id 184324" or "name 'Structural Model.rvt'".
        /// </summary>
        public static string Describe(LinkSelector selector)
        {
            return selector.InstanceId.HasValue
                ? $"id {selector.InstanceId.Value}"
                : $"name '{selector.Name}'";
        }
    }
}
