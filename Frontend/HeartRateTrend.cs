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
    /// <param name="strokeWidth">Curve thickness in device-independent pixels.</param>
    /// <param name="showAxis">Shows BPM ticks and grid lines on the dashboard.</param>
    /// <returns>The formatted BPM range, or an empty string without samples.</returns>
    internal static string Draw(Canvas chart, Queue<(double Time, int Bpm)> history, double now, double strokeWidth = 2.5, bool showAxis = false)
    {
        chart.Children.Clear();
        if (history.Count == 0) return "";
        var min = history.Min(sample => sample.Bpm);
        var max = history.Max(sample => sample.Bpm);
        var label = min == max ? min + " BPM" : $"{min}–{max} BPM";
        if (chart.ActualWidth <= 8 || chart.ActualHeight <= 8) return label;
        var floor = min - 3;
        var ceiling = max + 3;
        var left = showAxis ? 37 : 4;
        var inset = showAxis ? 10 : 4;
        var plotWidth = chart.ActualWidth - left - 4;
        var plotHeight = chart.ActualHeight - inset * 2;
        if (plotWidth <= 0 || plotHeight <= 0) return label;

        if (showAxis)
        {
            // Round the rolling data range outward to readable integer BPM ticks.
            var step = Math.Max(2, (int)Math.Ceiling((ceiling - floor) / 20.0) * 5);
            floor = Math.Max(0, (int)Math.Floor(floor / (double)step) * step);
            ceiling = (int)Math.Ceiling(ceiling / (double)step) * step;
            for (var tick = floor; tick <= ceiling; tick += step)
            {
                var y = inset + (ceiling - tick) * plotHeight / (ceiling - floor);
                chart.Children.Add(new Line { X1 = left, X2 = left + plotWidth, Y1 = y, Y2 = y,
                    Stroke = Brushes.LightSteelBlue, StrokeThickness = 1, Opacity = 0.5 });
                var caption = new TextBlock { Text = tick.ToString(), Width = left - 6,
                    TextAlignment = TextAlignment.Right, FontSize = 11, Foreground = Brushes.SlateGray };
                caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetTop(caption, y - caption.DesiredSize.Height / 2);
                chart.Children.Add(caption);
            }
        }

        var points = new PointCollection();
        foreach (var sample in history)
        {
            var progress = Math.Clamp(1 - (now - sample.Time) / HistorySeconds, 0, 1);
            var x = left + progress * plotWidth;
            var y = inset + (ceiling - sample.Bpm) * plotHeight / (ceiling - floor);
            points.Add(new Point(x, y));
        }

        var pink = new SolidColorBrush(Color.FromRgb(196, 52, 107));
        chart.Children.Add(new Polyline { Points = points, Stroke = pink, StrokeThickness = strokeWidth,
            StrokeLineJoin = PenLineJoin.Round });
        var latest = points[points.Count - 1];
        var dotSize = strokeWidth * 2.8;
        var dot = new Ellipse { Width = dotSize, Height = dotSize, Fill = pink };
        Canvas.SetLeft(dot, latest.X - dotSize / 2);
        Canvas.SetTop(dot, latest.Y - dotSize / 2);
        chart.Children.Add(dot);
        return label;
    }
}
