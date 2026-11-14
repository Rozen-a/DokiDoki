// Displays the current session in a draggable, always-on-top window.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DokiDoki.Frontend;

/// <summary>Shows a compact view of the same readings as the dashboard.</summary>
public sealed partial class HeartRateOverlay : Window
{
    private const int ExtendedStyleIndex = -20;
    private const int TransparentStyle = 0x20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowStyle(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowStyle(IntPtr window, int index, int value);

    private bool positionLocked;
    private Queue<(double Time, int Bpm)> samples;
    private double now;

    /// <summary>Initializes the floating window layout.</summary>
    public HeartRateOverlay()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyInputStyle();
    }

    /// <summary>Enables native mouse pass-through for the locked layered window.</summary>
    private void ApplyInputStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowStyle(handle, ExtendedStyleIndex);
        var updatedStyle = positionLocked ? style | TransparentStyle : style & ~TransparentStyle;
        if (updatedStyle != style) SetWindowStyle(handle, ExtendedStyleIndex, updatedStyle);
    }

    /// <summary>Applies the dashboard display controls to the floating window.</summary>
    /// <param name="heartOnRight">Places the heart on the right when true.</param>
    /// <param name="showTrend">Shows the rolling heart-rate chart when true.</param>
    /// <param name="showBackground">Shows a translucent chart background when true.</param>
    /// <param name="locked">Prevents the window from being moved when true.</param>
    internal void ApplyOptions(bool heartOnRight, bool showTrend, bool showBackground, bool locked)
    {
        Grid.SetColumn(heart, heartOnRight ? 2 : 0);
        Grid.SetColumn(rate, heartOnRight ? 0 : 2);
        trendPanel.Visibility = showTrend ? Visibility.Visible : Visibility.Collapsed;
        trendPanel.Background = showBackground
            ? new SolidColorBrush(Color.FromArgb(156, 221, 238, 255))
            : Brushes.Transparent;
        positionLocked = locked;
        ApplyInputStyle();
        Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        DrawTrend();
    }

    /// <summary>Updates the BPM and redraws the shared trend on the UI thread.</summary>
    /// <param name="bpm">Current BPM display text.</param>
    /// <param name="history">The dashboard's rolling history.</param>
    /// <param name="timestamp">Current elapsed seconds on the dashboard clock.</param>
    internal void UpdateReading(string bpm, Queue<(double Time, int Bpm)> history, double timestamp)
    {
        bpmText.Text = bpm;
        heart.SetRate(int.TryParse(bpm, out var value) ? value : 0);
        samples = history;
        now = timestamp;
        DrawTrend();
    }

    /// <summary>Redraws the trend when data or the canvas bounds change.</summary>
    private void DrawTrend()
    {
        if (trendHint == null || samples == null || trendPanel.Visibility != Visibility.Visible) return;
        trendHint.Visibility = samples.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HeartRateTrend.Draw(trend, samples, now, strokeWidth: 1.25);
    }

    /// <summary>Refreshes the curve after the chart is measured.</summary>
    /// <param name="sender">The chart canvas.</param>
    /// <param name="args">The WPF size event.</param>
    private void TrendSizeChanged(object sender, SizeChangedEventArgs args) => DrawTrend();

    /// <summary>Moves the window while the left mouse button is held and the position is unlocked.</summary>
    /// <param name="sender">The floating window surface.</param>
    /// <param name="args">The WPF mouse event.</param>
    private void DragSurface(object sender, MouseButtonEventArgs args)
    {
        if (!positionLocked && args.LeftButton == MouseButtonState.Pressed)
        {
            args.Handled = true;
            DragMove();
        }
    }
}
