using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Mechanical
{
    /// <summary>
    /// Lists all HVAC mechanical system types (MechanicalSystemType) in the
    /// active document. Ducts require a system type at creation time — use
    /// this command to discover the <c>system_type_id</c> for
    /// <c>create_duct</c>.
    /// </summary>
    public class GetDuctSystemTypesHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_duct_system_types";
        public override string Description => "Lists all HVAC mechanical system types available in the active document";
        public override string Category => "Query";
        public override string[] Aliases => new[] { "duct_system_types", "list_duct_system_types" };

        protected override string ItemsProperty => "system_types";
        protected override string SuccessMessage(int count) => $"Retrieved {count} duct system type(s).";

        protected override CommandParamSchema[] BaseParameters => System.Array.Empty<CommandParamSchema>();

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_duct_system_types\", \"parameters\": {} }"
        };

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(MechanicalSystemType))
                .Cast<MechanicalSystemType>()
                .OrderBy(t => t.Id.IntegerValue)
                .Select(t => (object)new
                {
                    system_type_id = t.Id.IntegerValue,
                    name = t.Name,
                    classification = t.SystemClassification.ToString()
                });
        }
    }
}
