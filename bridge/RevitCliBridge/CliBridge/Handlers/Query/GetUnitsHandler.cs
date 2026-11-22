using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers.Query
{
    /// <summary>
    /// Answers the active document's unit matrix in units-contract
    /// vocabulary: what the "project" sentinel would resolve to, the
    /// canonical defaults, and the units allowed per kind, so a caller
    /// can compose an explicit units map instead of relying on defaults.
    /// </summary>
    public class GetUnitsHandler : DocumentCommandBase
    {
        public override string CommandName => "get_units";
        public override string Description => "Returns the active document's units per kind (what the \"project\" sentinel resolves to), plus the canonical defaults and the units allowed per kind";
        public override string Category => "Query";

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_units\", \"parameters\": {} }"
        };

        protected override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var canonical = UnitsContext.CanonicalMap();
            var allowed = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var kind in canonical.Keys)
                allowed[kind] = UnitContracts.AllowedUnits(kind);

            var result = new
            {
                project = UnitsContext.ReadProjectUnits(doc),
                canonical = canonical,
                allowed = allowed
            };

            return CommandResponse.Success(cmd.TaskId, result,
                "Retrieved units matrix.").ToJson();
        }
    }
}
