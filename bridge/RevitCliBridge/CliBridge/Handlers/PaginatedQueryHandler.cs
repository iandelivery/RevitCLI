using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using System.Collections.Generic;

namespace RevitCliBridge.Handlers
{
    /// <summary>
    /// Template-method base for query handlers that return potentially large
    /// element collections. Centralizes pagination mechanics (limit/offset
    /// parsing, Skip/Take, has_more detection) so individual handlers only
    /// declare their filtering/projection logic.
    ///
    /// Subclasses must:
    /// 1. Override <see cref="QuerySource"/> to return a filtered, projected,
    ///    and <em>already-ordered</em> sequence (stable key required for
    ///    correct pagination).
    /// 2. Override <see cref="BaseParameters"/> to declare handler-specific
    ///    parameters (limit/offset are injected automatically).
    /// 3. Optionally override <see cref="ItemsProperty"/>,
    ///    <see cref="SuccessMessage"/>, or <see cref="MergeExtraFields"/>.
    /// </summary>
    public abstract class PaginatedQueryHandler<TItem> : DocumentCommandBase
    {
        /// <summary>
        /// JSON property name for the items array in the response envelope.
        /// Defaults to "items"; override for domain-specific names ("elements",
        /// "types", "symbols", …).
        /// </summary>
        protected virtual string ItemsProperty => "items";

        /// <summary>
        /// Handler-specific parameters (limit/offset are appended automatically).
        /// </summary>
        protected abstract CommandParamSchema[] BaseParameters { get; }

        /// <summary>
        /// Final <see cref="Parameters"/> schema: BaseParameters + limit + offset.
        /// Sealed so subclasses can't accidentally drop the paging params.
        /// </summary>
        public sealed override CommandParamSchema[] Parameters
        {
            get
            {
                var baseParams = BaseParameters;
                var result = new CommandParamSchema[baseParams.Length + 2];
                var i = 0;
                for (; i < baseParams.Length; i++)
                    result[i] = baseParams[i];
                result[i] = new CommandParamSchema
                {
                    Name = "limit",
                    Type = "int",
                    Required = false,
                    Description = "Maximum number of results (page size)",
                    Default = PagedResultBuilder.DefaultLimit
                };
                result[i + 1] = new CommandParamSchema
                {
                    Name = "offset",
                    Type = "int",
                    Required = false,
                    Description = "Number of results to skip for pagination",
                    Default = PagedResultBuilder.DefaultOffset
                };
                return result;
            }
        }

        /// <summary>
        /// Returns the filtered, projected, and <em>ordered</em> source
        /// sequence. The base class applies Skip/Take and has_more detection
        /// on top of this. The sequence MUST be ordered by a stable key
        /// (e.g. element id) for correct pagination.
        /// </summary>
        protected abstract IEnumerable<TItem> QuerySource(UIApplication app, Document doc, Dictionary<string, object> parameters);

        /// <summary>
        /// Hook for subclasses to inject extra envelope fields (e.g.
        /// search_elements echoes the filter criteria). Default: no-op.
        /// </summary>
        protected virtual void MergeExtraFields(Dictionary<string, object> result, Dictionary<string, object> parameters) { }

        /// <summary>
        /// Pre-query validation hook. Return an error message to short-circuit
        /// with an error response, or null to proceed. Default: no validation.
        /// </summary>
        protected virtual string? Validate(Dictionary<string, object> parameters) => null;

        /// <summary>
        /// Success message shown to the caller. Default includes the item count.
        /// </summary>
        protected virtual string SuccessMessage(int count) => $"Retrieved {count} items.";

        protected sealed override string Execute(UIApplication app, Document doc, Dictionary<string, object> parameters, QueuedCommand cmd)
        {
            var error = Validate(parameters);
            if (error is not null)
                return CommandResponse.Error(cmd.TaskId, error).ToJson();

            var (limit, offset) = PagedResultBuilder.GetPagingParams(parameters);
            var source = QuerySource(app, doc, parameters);
            var paged = PagedResultBuilder.Build(source, limit, offset);

            var result = new Dictionary<string, object>
            {
                ["count"] = paged.Count,
                ["offset"] = paged.Offset,
                ["limit"] = paged.Limit,
                ["has_more"] = paged.HasMore,
                [ItemsProperty] = paged.Items
            };
            MergeExtraFields(result, parameters);

            return CommandResponse.Success(cmd.TaskId, result, SuccessMessage(paged.Count)).ToJson();
        }
    }
}
