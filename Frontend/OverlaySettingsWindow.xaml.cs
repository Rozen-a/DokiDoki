// Edits floating-window options independently of the dashboard.
using System;
using System.Windows.Controls;
using System.Windows;
using DokiDoki.Backend;

namespace DokiDoki.Frontend;

/// <summary>Stores the floating-window display options between application sessions.</summary>
internal record OverlayOptions(bool Enabled = false, bool HeartRight = false,
    bool ShowTrend = true, bool ShowBackground = true, bool Locked = false, bool Capture = false, bool HideBpm = false, bool StartupConnect = true, bool Reconnect = true, double? OverlayLeft = null, double? OverlayTop = null, int? StartupSeconds = null, int? ReconnectSeconds = null);

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

        Func<int> startupLimit = () => options.StartupSeconds ?? 0;
        Func<int> reconnectLimit = () => options.ReconnectSeconds ?? 120;
        startupLimit = ConfigureDuration(startupDuration, startupSeconds, startupSecondsUnit,
            options.StartupSeconds ?? 0, Apply);
        reconnectLimit = ConfigureDuration(reconnectDuration, reconnectSeconds, reconnectSecondsUnit,
            options.ReconnectSeconds ?? 120, Apply);

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
                Reconnect = reconnect.IsChecked == true,
                StartupSeconds = startupLimit(),
                ReconnectSeconds = reconnectLimit()
            });
    }

    /// <summary>Configures one unlimited or timed connection policy.</summary>
    /// <param name="mode">Selector for unlimited or timed operation.</param>
    /// <param name="input">Positive whole seconds for timed operation.</param>
    /// <param name="unit">Seconds label shown beside the input.</param>
    /// <param name="savedSeconds">Previously saved value, with zero meaning unlimited.</param>
    /// <param name="apply">Callback that persists a valid change.</param>
    /// <returns>A function returning the current effective limit in seconds.</returns>
    private static Func<int> ConfigureDuration(ComboBox mode, TextBox input, TextBlock unit,
        int savedSeconds, RoutedEventHandler apply)
    {
        // CancellationTokenSource's timer is limited to uint.MaxValue minus one milliseconds.
        const int maxSeconds = 4_294_967;
        int seconds = savedSeconds is > 0 and <= maxSeconds ? savedSeconds : 120;
        mode.SelectedIndex = savedSeconds > 0 ? 1 : 0;
        input.Text = seconds.ToString();
        UpdateVisibility();

        mode.SelectionChanged += (sender, args) =>
        {
            UpdateVisibility();
            apply(sender, args);
        };
        input.TextChanged += (sender, args) =>
        {
            if (int.TryParse(input.Text, out int value) && value is > 0 and <= maxSeconds)
            {
                input.ClearValue(ToolTipProperty);
                if (seconds == value) return;
                seconds = value;
                apply(sender, args);
            }
            else input.ToolTip = "请输入 1 至 4294967 的整数秒数。";
        };
        // Keep the last valid value when the field is left empty or invalid.
        input.LostFocus += (_, _) => input.Text = seconds.ToString();
        return () => mode.SelectedIndex == 1 ? seconds : 0;

        void UpdateVisibility()
        {
            input.Visibility = unit.Visibility = mode.SelectedIndex == 1
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
