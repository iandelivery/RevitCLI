using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitCliBridge.Handlers
{
    /// <summary>
    /// Auto-suppresses warnings and resolves errors during Transaction
    /// operations, collecting the description of every resolved error so
    /// handlers can surface it as resolved_failures in the response.
    /// </summary>
    public class CliFailurePreprocessor : IFailuresPreprocessor
    {
        /// <summary>
        /// Descriptions of the errors this preprocessor resolved. Callers
        /// that want the collection pass their own list through
        /// <c>ConfigureFailureHandling(transaction, list)</c>.
        /// </summary>
        public List<string> ResolvedErrors { get; }

        public CliFailurePreprocessor(List<string>? resolvedErrors = null)
        {
            ResolvedErrors = resolvedErrors ?? new List<string>();
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool resolvedError = false;

            foreach (var failure in failuresAccessor.GetFailureMessages())
            {
                var severity = failure.GetSeverity();

                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else if (severity == FailureSeverity.Error)
                {
                    ResolvedErrors.Add(failure.GetDescriptionText());
                    failuresAccessor.ResolveFailure(failure);
                    resolvedError = true;
                }
            }

            return resolvedError
                ? FailureProcessingResult.ProceedWithCommit
                : FailureProcessingResult.Continue;
        }
    }
}
