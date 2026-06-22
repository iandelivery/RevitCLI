using Newtonsoft.Json;
using Autodesk.Revit.DB;

namespace RevitCliBridge.Handlers
{
    /// <summary>
    /// Auto-suppresses warnings and handles errors during Transaction operations.
    /// </summary>
    public class CliFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failures = failuresAccessor.GetFailureMessages();

            foreach (var failure in failures)
            {
                var severity = failure.GetSeverity();

                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else if (severity == FailureSeverity.Error)
                {
                    failuresAccessor.ResolveFailure(failure);
                    return FailureProcessingResult.ProceedWithCommit;
                }
            }

            return FailureProcessingResult.Continue;
        }
    }
}
