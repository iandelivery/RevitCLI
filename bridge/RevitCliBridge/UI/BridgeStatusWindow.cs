using RevitCliBridge;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RevitCliBridge
{
    /// <summary>
    /// Modeless WPF window showing the CLI Bridge state with a toggle button.
    /// The toggle action is dispatched through ToggleBridgeEventHandler because
    /// a modeless window click handler is not a valid Revit API context, and
    /// Toggle() internally calls ExternalEvent.Create.
    /// A DispatcherTimer keeps the status area in sync while the window is open.
    /// </summary>
    public class BridgeStatusWindow : Window
    {
        private static BridgeStatusWindow? _current;

        private static readonly string FileVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "Unknown";

        private const string AuthorName = "CHEN YIN";

        private readonly Ellipse _statusDot;
        private readonly TextBlock _statusText;
        private readonly TextBlock _detailText;
        private readonly Button _toggleButton;
        private readonly DispatcherTimer _timer;

        /// <summary>
        /// Show the status window (or focus it if already open).
        /// </summary>
        /// <param name="ownerHandle">Revit main window handle, so the modeless
        /// window stays above Revit and does not appear in Alt-Tab as orphan.</param>
        public static void ShowWindow(IntPtr ownerHandle)
        {
            if (_current != null)
            {
                _current.Activate();
                return;
            }

            var window = new BridgeStatusWindow();
            var helper = new System.Windows.Interop.WindowInteropHelper(window) { Owner = ownerHandle };
            window.Show();
        }

        private BridgeStatusWindow()
        {
            Title = $"Revit CLI Bridge v{FileVersion}";
            Width = 340;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;

            var header = new TextBlock
            {
                Text = "AI Mode",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };

            _statusDot = new Ellipse
            {
                Width = 12,
                Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            _statusText = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
            statusRow.Children.Add(_statusDot);
            statusRow.Children.Add(_statusText);

            _detailText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            _toggleButton = new Button
            {
                MinHeight = 34,
                Margin = new Thickness(0, 14, 0, 0),
                FontWeight = FontWeights.SemiBold
            };
            _toggleButton.Click += OnToggleClick;

            var author = new TextBlock
            {
                Text = $"by {AuthorName}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(header);
            root.Children.Add(statusRow);
            root.Children.Add(_detailText);
            root.Children.Add(_toggleButton);
            root.Children.Add(author);

            Content = root;

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += (s, e) => Refresh();
            _timer.Start();

            Closed += (s, e) =>
            {
                _timer.Stop();
                _current = null;
            };

            Refresh();
        }

        private void OnToggleClick(object sender, RoutedEventArgs e)
        {
            _toggleButton.IsEnabled = false;
            try
            {
                var toggleEvent = ToggleBridgeEventHandler.ToggleEvent;
                if (toggleEvent != null)
                {
                    toggleEvent.Raise();
                }
                else
                {
                    // Fallback (event creation failed at startup): the click may
                    // still be inside a valid API context when opened from the
                    // ribbon command, otherwise this will throw and be logged.
                    CliBridgeStateManager.Toggle();
                }
            }
            catch (Exception ex)
            {
                CliLogger.Error($"Bridge toggle failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Re-read bridge state and update every visual; safe to call repeatedly.
        /// </summary>
        private void Refresh()
        {
            bool enabled = false;
            try { enabled = CliBridgeStateManager.IsEnabled; } catch { }

            int port = 0;
            try { if (enabled) port = CliBridgeStateManager.ActualPort; } catch { }

            int revitVersion = 0;
            try { revitVersion = CliBridgeStateManager.RevitVersion; } catch { }

            _statusDot.Fill = new SolidColorBrush(enabled
                ? Color.FromRgb(0x2E, 0xA0, 0x43)
                : Color.FromRgb(0x9E, 0x9E, 0x9E));
            _statusText.Text = enabled ? "Enabled" : "Disabled";
            _detailText.Text = enabled
                ? $"Revit {revitVersion}{(port > 0 ? $" · port {port}" : "")} · AI agents can communicate with Revit"
                : "AI agents cannot communicate with Revit";
            _toggleButton.Content = enabled ? "Disable AI Mode" : "Enable AI Mode";
            _toggleButton.IsEnabled = true;
        }
    }
}
