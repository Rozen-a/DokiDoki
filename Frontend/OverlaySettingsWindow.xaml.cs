// Edits floating-window options independently of the dashboard.
using System;
using System.Windows;
using DokiDoki.Backend;

namespace DokiDoki.Frontend;

/// <summary>Stores the floating-window display options for the current application session.</summary>
internal record OverlayOptions(bool Enabled = false, bool HeartRight = false,
    bool ShowTrend = true, bool ShowBackground = true, bool Locked = false, bool Capture = false);

/// <summary>Applies floating-window settings as the user changes each option.</summary>
public sealed partial class OverlaySettingsWindow : Window
{
    /// <summary>Loads the current options and forwards any subsequent changes.</summary>
    /// <param name="options">The settings to display on opening.</param>
    /// <param name="apply">The callback that applies a new settings snapshot.</param>
    internal OverlaySettingsWindow(OverlayOptions options, Action<OverlayOptions> apply)
    {
        InitializeComponent();
        enabled.IsChecked = options.Enabled;
        heartRight.IsChecked = options.HeartRight;
        rateRight.IsChecked = !options.HeartRight;
        showTrend.IsChecked = options.ShowTrend;
        showBackground.IsChecked = options.ShowBackground;
        locked.IsChecked = options.Locked;

        capture.IsChecked = options.Capture;
        captureAddress.Text = ObsCaptureServer.Address;
        RefreshCaptureControls();

        foreach (var option in new[] { enabled, showTrend, showBackground, locked, capture })
        {
            option.Checked += Apply;
            option.Unchecked += Apply;
        }

        heartRight.Checked += Apply;
        rateRight.Checked += Apply;

        // Apply each complete snapshot and clear the streaming toggle if binding fails.
        void Apply(object sender, RoutedEventArgs args)
        {
            var snapshot = new OverlayOptions(enabled.IsChecked == true, heartRight.IsChecked == true,
                showTrend.IsChecked == true, showBackground.IsChecked == true, locked.IsChecked == true,
                capture.IsChecked == true);
            captureError.Text = "";
            try { apply(snapshot); }
            catch (System.Net.HttpListenerException error)
            {
                capture.Unchecked -= Apply;
                capture.IsChecked = false;
                capture.Unchecked += Apply;
                apply(snapshot with { Capture = false });
                captureError.Text = "无法开启直播采集：" + error.Message;
            }
            RefreshCaptureControls();
        }
    }

    /// <summary>Shows the browser address and enables styling for either active output.</summary>
    private void RefreshCaptureControls()
    {
        var streaming = capture.IsChecked == true;
        captureLink.Visibility = streaming ? Visibility.Visible : Visibility.Collapsed;
        displayOptions.IsEnabled = streaming || enabled.IsChecked == true;
    }
}
