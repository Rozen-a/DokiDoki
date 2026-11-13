// Displays the current session in a draggable, always-on-top window.
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DokiDoki.Frontend;

/// <summary>Shows a compact view of the same readings as the dashboard.</summary>
public sealed partial class HeartRateOverlay : Window
{
    private bool positionLocked;
    private Queue<(double Time, int Bpm)> samples;
    private double now;

    /// <summary>Initializes the floating window layout.</summary>
    public HeartRateOverlay() => InitializeComponent();

    /// <summary>Applies the dashboard display controls to the floating window.</summary>
    /// <param name="heartOnRight">Places the heart on the right when true.</param>
    /// <param name="bpmOnRight">Places the BPM on the right when true.</param>
    /// <param name="showTrend">Shows the rolling heart-rate chart when true.</param>
    /// <param name="showBackground">Shows a translucent chart background when true.</param>
    /// <param name="locked">Prevents the window from being moved when true.</param>
    internal void ApplyOptions(bool heartOnRight, bool bpmOnRight, bool showTrend, bool showBackground, bool locked)
    {
        DockPanel.SetDock(heart, heartOnRight ? Dock.Right : Dock.Left);
        DockPanel.SetDock(rate, bpmOnRight ? Dock.Right : Dock.Left);
        trendPanel.Visibility = showTrend ? Visibility.Visible : Visibility.Collapsed;
        trendPanel.Background = showBackground
            ? new SolidColorBrush(Color.FromArgb(156, 221, 238, 255))
            : Brushes.Transparent;
        positionLocked = locked;
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
        samples = history;
        now = timestamp;
        DrawTrend();
    }

    /// <summary>Redraws the trend when data or the canvas bounds change.</summary>
    private void DrawTrend()
    {
        if (trendHint == null || samples == null || trendPanel.Visibility != Visibility.Visible) return;
        trendHint.Visibility = samples.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HeartRateTrend.Draw(trend, samples, now);
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
