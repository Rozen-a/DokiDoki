// Renders the same time-aligned heart-rate curve in both windows.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DokiDoki.Frontend;

/// <summary>Draws BPM samples over a fixed rolling time interval.</summary>
internal static class HeartRateTrend
{
    internal const double HistorySeconds = 60;

    /// <summary>Renders the latest samples and returns their observed BPM range.</summary>
    /// <param name="chart">The measured target canvas.</param>
    /// <param name="history">Timestamped samples in chronological order.</param>
    /// <param name="now">Current elapsed seconds on the sample clock.</param>
    /// <returns>The formatted BPM range, or an empty string without samples.</returns>
    internal static string Draw(Canvas chart, Queue<(double Time, int Bpm)> history, double now)
    {
        chart.Children.Clear();
        if (history.Count == 0) return "";
        var min = history.Min(sample => sample.Bpm);
        var max = history.Max(sample => sample.Bpm);
        var label = min == max ? min + " BPM" : $"{min}–{max} BPM";
        if (chart.ActualWidth <= 8 || chart.ActualHeight <= 8) return label;
        var floor = min - 3;
        var ceiling = max + 3;
        var points = new PointCollection();
        foreach (var sample in history)
        {
            var progress = Math.Clamp(1 - (now - sample.Time) / HistorySeconds, 0, 1);
            var x = 4 + progress * (chart.ActualWidth - 8);
            var y = 4 + (ceiling - sample.Bpm) * (chart.ActualHeight - 8) / (ceiling - floor);
            points.Add(new Point(x, y));
        }

        var pink = new SolidColorBrush(Color.FromRgb(196, 52, 107));
        chart.Children.Add(new Polyline { Points = points, Stroke = pink, StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round });
        var latest = points[points.Count - 1];
        var dot = new Ellipse { Width = 7, Height = 7, Fill = pink };
        Canvas.SetLeft(dot, latest.X - 3.5);
        Canvas.SetTop(dot, latest.Y - 3.5);
        chart.Children.Add(dot);
        return label;
    }
}
