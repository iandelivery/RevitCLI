using Autodesk.Revit.UI;
using RevitCliBridge;
using System;

namespace RevitCliBridge
{
    /// <summary>
    /// External event handler that toggles the CLI Bridge on/off.
    /// Toggle() calls ExternalEvent.Create internally (StartBridge), which
    /// requires a valid Revit API context — modeless UI is not one, so the
    /// status window raises this event instead of calling Toggle() directly.
    /// The ExternalEvent is created once in BridgeApp.OnStartup.
    /// </summary>
    public class ToggleBridgeEventHandler : IExternalEventHandler
    {
        /// <summary>
        /// Event created in BridgeApp.OnStartup; raised by the status window.
        /// </summary>
        public static ExternalEvent? ToggleEvent;

        /// <summary>
        /// Shared handler instance passed to ExternalEvent.Create.
        /// </summary>
        public static readonly ToggleBridgeEventHandler Instance = new();

        public void Execute(UIApplication app)
        {
            try
            {
                CliBridgeStateManager.Toggle();
            }
            catch (Exception ex)
            {
                CliLogger.Error($"Bridge toggle via external event failed: {ex.Message}");
            }
        }

        public string GetName() => "Toggle CLI Bridge";
    }
}
