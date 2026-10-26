using Autodesk.Revit.UI;
using RevitCliBridge;
using System;
using System.IO;
using System.Reflection;

namespace RevitCliBridge
{
    /// <summary>
    /// Revit external application entry point for the CLI Bridge add-in.
    /// Initializes the HTTP server and creates the Ribbon UI.
    /// </summary>
    public class BridgeApp : IExternalApplication
    {
        public const string TabName = "Revit CLI Bridge";
        public const string PanelName = "AI Tools";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // Resolve assemblies from the add-in directory
                AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

                // Detect Revit version from the controlled application.
                int revitVersion = 0;
                try
                {
                    var versionString = application.ControlledApplication.VersionNumber;
                    int.TryParse(versionString, out revitVersion);
                }
                catch { /* version detection is best-effort */ }

                // Initialize the CLI Bridge HTTP server with version info
                CliBridgeStateManager.Initialize(revitVersion);

                // Create the toggle external event in a valid API context so
                // the modeless status window can request a bridge toggle later.
                try
                {
                    ToggleBridgeEventHandler.ToggleEvent =
                        ExternalEvent.Create(ToggleBridgeEventHandler.Instance);
                }
                catch (Exception ex)
                {
                    CliLogger.Warn($"Failed to create toggle external event: {ex.Message}");
                    ToggleBridgeEventHandler.ToggleEvent = null;
                }

                // Create Ribbon UI
                try
                {
                    application.CreateRibbonTab(TabName);
                }
                catch (Exception)
                {
                    // Tab already exists, ignore
                }

                RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);

                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                string directoryName = Path.GetDirectoryName(assemblyPath) ?? "";

                // Add the "AI Mode Toggle" button
                var pushButtonData = new PushButtonData(
                    "ToggleCliBridge",
                    "AI模式",
                    assemblyPath,
                    "RevitCliBridge.ToggleBridgeCommand")
                {
                    ToolTip = "Enable or disable AI mode (CLI Bridge HTTP server)",
                    LargeImage = LoadIcon("icons/icon_cli-bridge.png"),
                    Image = LoadIcon("icons/icon_cli-bridge-s.png")
                };

                panel.AddItem(pushButtonData);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Revit CLI Bridge", "Failed to start: " + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            CliBridgeStateManager.Cleanup();
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
            return Result.Succeeded;
        }

        private Assembly? CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);
            string location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            var file = Path.Combine(location, $"{assemblyName.Name}.dll");
            if (File.Exists(file))
            {
                return Assembly.LoadFrom(file);
            }
            return null;
        }

        /// <summary>
        /// Load a PNG embedded as a WPF Resource into the add-in assembly.
        /// Returns null on failure so the button simply renders without an icon.
        /// </summary>
        private static System.Windows.Media.Imaging.BitmapImage? LoadIcon(string relativePath)
        {
            try
            {
                var uri = new Uri(
                    $"pack://application:,,,/RevitCliBridge;component/{relativePath}",
                    UriKind.Absolute);
                return new System.Windows.Media.Imaging.BitmapImage(uri);
            }
            catch (Exception ex)
            {
                CliLogger.Warn($"Failed to load icon '{relativePath}': {ex.Message}");
                return null;
            }
        }
    }
}
