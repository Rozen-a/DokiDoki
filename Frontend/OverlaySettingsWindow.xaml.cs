// Edits floating-window options independently of the dashboard.
using System;
using System.Windows;
using DokiDoki.Backend;

namespace DokiDoki.Frontend;

/// <summary>Stores the floating-window display options between application sessions.</summary>
internal record OverlayOptions(bool Enabled = false, bool HeartRight = false,
    bool ShowTrend = true, bool ShowBackground = true, bool Locked = false, bool Capture = false, bool HideBpm = false, bool StartupConnect = true, bool Reconnect = true, double? OverlayLeft = null, double? OverlayTop = null);

/// <summary>Applies floating-window settings as the user changes each option.</summary>
public sealed partial class OverlaySettingsWindow : Window
{
    /// <summary>Loads the current options and forwards any subsequent changes.</summary>
    /// <param name="options">The settings to display on opening.</param>
    /// <param name="apply">The callback that applies a new settings snapshot.</param>
    internal OverlaySettingsWindow(OverlayOptions options, Action<OverlayOptions> apply)
    {
        InitializeComponent();
        heartRight.IsChecked = options.HeartRight;
        rateRight.IsChecked = !options.HeartRight;
        showTrend.IsChecked = options.ShowTrend;
        showBackground.IsChecked = options.ShowBackground;
        locked.IsChecked = options.Locked;
        hideBpm.IsChecked = options.HideBpm;
        startup.IsChecked = options.StartupConnect;
        reconnect.IsChecked = options.Reconnect;
        captureAddress.Text = ObsCaptureServer.Address;

        foreach (var option in new[] { showTrend, showBackground, locked, hideBpm, startup, reconnect })
        {
            option.Checked += Apply;
            option.Unchecked += Apply;
        }
        heartRight.Checked += Apply;
        rateRight.Checked += Apply;

        // Publish a snapshot whenever a setting changes.
        void Apply(object sender, RoutedEventArgs args) =>
            apply(options with
            {
                HeartRight = heartRight.IsChecked == true,
                ShowTrend = showTrend.IsChecked == true,
                ShowBackground = showBackground.IsChecked == true,
                Locked = locked.IsChecked == true,
                HideBpm = hideBpm.IsChecked == true,
                StartupConnect = startup.IsChecked == true,
                Reconnect = reconnect.IsChecked == true
            });
    }
}
