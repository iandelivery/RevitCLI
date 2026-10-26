using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge;
using System;

namespace RevitCliBridge
{
    /// <summary>
    /// External command to open the modeless Bridge status window.
    /// The window shows the current bridge state and hosts the toggle button;
    /// toggling is dispatched through ToggleBridgeEventHandler (see window).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ToggleBridgeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                BridgeStatusWindow.ShowWindow(commandData.Application.MainWindowHandle);
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
