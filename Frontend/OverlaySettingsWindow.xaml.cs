// Edits floating-window options independently of the dashboard.
using System;
using System.Windows;

namespace DokiDoki.Frontend;

/// <summary>Stores the floating-window display options for the current application session.</summary>
internal record OverlayOptions(bool Enabled = false, bool HeartRight = false,
    bool ShowTrend = true, bool ShowBackground = true, bool Locked = false);

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

        foreach (var option in new[] { enabled, showTrend, showBackground, locked })
        {
            option.Checked += Apply;
            option.Unchecked += Apply;
        }

        heartRight.Checked += Apply;
        rateRight.Checked += Apply;

        // Read the controls together so reopening the window restores one consistent snapshot.
        void Apply(object sender, RoutedEventArgs args) =>
            apply(new OverlayOptions(enabled.IsChecked == true, heartRight.IsChecked == true,
                showTrend.IsChecked == true, showBackground.IsChecked == true,
                locked.IsChecked == true));
    }
}
