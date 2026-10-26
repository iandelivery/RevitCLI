using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;

namespace RevitCliBridge.Handlers.Piping
{
    /// <summary>
    /// Lists all pipe types (PipeType) available in the active document.
    /// Read-only command — useful for discovering the <c>pipe_type_id</c>
    /// to pass to <c>create_pipe</c>.
    /// </summary>
    public class GetPipeTypesHandler : PaginatedQueryHandler<object>
    {
        public override string CommandName => "get_pipe_types";
        public override string Description => "Lists all pipe types available in the active document";
        public override string Category => "Query";
        public override string[] Aliases => new[] { "pipe_types", "list_pipe_types" };

        protected override string ItemsProperty => "types";
        protected override string SuccessMessage(int count) => $"Retrieved {count} pipe type(s).";

        protected override CommandParamSchema[] BaseParameters => System.Array.Empty<CommandParamSchema>();

        public override string[] Examples => new[]
        {
            "{ \"command\": \"get_pipe_types\", \"parameters\": {} }",
            "{ \"command\": \"get_pipe_types\", \"parameters\": { \"limit\": 50 } }"
        };

        protected override IEnumerable<object> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .OrderBy(t => t.Id.IntegerValue)
                .Select(t => (object)new
                {
                    type_id = t.Id.IntegerValue,
                    name = t.Name
                });
        }
    }
}