using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge;
using System;
using System.Reflection;

namespace RevitCliBridge
{
    /// <summary>
    /// External command to toggle the CLI Bridge on/off.
    /// Users can enable or disable the CLI Bridge via this command.
    /// Shows a dialog with the current state after toggling.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ToggleBridgeCommand : IExternalCommand
    {
        private static readonly string FileVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "Unknown";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                bool newState = CliBridgeStateManager.Toggle();

                string title = $"AI Mode Status v{FileVersion}";
                string instruction = newState ? "AI Mode Enabled" : "AI Mode Disabled";
                string content = newState
                    ? "AI Mode is enabled.\nAI agents can communicate with Revit."
                    : "AI Mode is disabled.\nAI agents cannot communicate with Revit.";

                TaskDialog dialog = new TaskDialog(title);
                dialog.MainInstruction = instruction;
                dialog.MainContent = content;
                dialog.MainIcon = newState ? TaskDialogIcon.TaskDialogIconInformation : TaskDialogIcon.TaskDialogIconWarning;
                dialog.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("AI Mode Toggle Failed", "Error: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
