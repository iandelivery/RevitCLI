using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Piping
{
    /// <summary>
    /// Lists all piping system types (PipingSystemType) in the active
    /// document. Pipes require a system type at creation time — use this
    /// command to discover the <c>system_type_id</c> for <c>create_pipe</c>.
    /// Optionally filters by system classification (e.g. DomesticColdWater,
    /// Sanitary, FireProtectWet).
    /// </summary>
    public class GetPipeSystemTypesHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_pipe_system_types";
        public override string Description => "Lists all piping system types available in the active document";
        public override string Category => "Query";
        public override string[] Aliases => new[] { "pipe_system_types", "list_pipe_system_types" };

        protected override string ItemsProperty => "system_types";
        protected override string SuccessMessage(int count) => $"Retrieved {count} pipe system type(s).";

        protected override CommandParamSchema[] BaseParameters => new[]
        {
            new CommandParamSchema
            {
                Name = "system_class",
                Type = "string",
                Required = false,
                Description = "Filter by system classification (e.g. DomesticColdWater, Sanitary, FireProtectWet)"
            }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_pipe_system_types\", \"parameters\": {} }",
            "{ \"command\": \"get_pipe_system_types\", \"parameters\": { \"system_class\": \"Sanitary\" } }"
        };

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            string? systemClass = HandlerUtilities.GetStringOrNull(parameters, "system_class");

            IEnumerable<PipingSystemType> result = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>();

            if (!string.IsNullOrEmpty(systemClass))
            {
                var wanted = systemClass!.Trim().ToLowerInvariant();
                result = result.Where(t => t.SystemClassification.ToString().ToLowerInvariant() == wanted);
            }

            return result
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