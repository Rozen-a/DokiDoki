// Animates the shared pixel heart without resetting its phase when BPM changes.
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DokiDoki.Frontend;

/// <summary>Scales the heart around its center without changing the layout.</summary>
public sealed class PulsingHeart : Image
{
    private readonly ScaleTransform scale = new(1, 1);
    private int targetBpm;
    private double currentBpm;
    private double phase;
    private long lastFrame;
    private bool playing;

    /// <summary>Initializes the centered transform and tracks when rendering is needed.</summary>
    public PulsingHeart()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = scale;
        Loaded += (_, _) => UpdatePlayback();
        Unloaded += (_, _) => UpdatePlayback();
        IsVisibleChanged += (_, _) => UpdatePlayback();
    }

    /// <summary>Sets the target animation tempo without interrupting the current beat.</summary>
    /// <param name="value">Current beats per minute, or zero when no reading is available.</param>
    internal void SetRate(int value)
    {
        var tempo = GetTempo(value);
        if (targetBpm == tempo) return;
        targetBpm = tempo;
        UpdatePlayback();
    }

    /// <summary>Maps measured BPM to a shared visual animation tempo.</summary>
    /// <param name="value">Measured BPM, or zero without a reading.</param>
    /// <returns>Beats per minute for the animation.</returns>
    internal static int GetTempo(int value) => value switch
    {
        <= 0 => 0,
        < 60 => 50,
        < 80 => 70,
        < 100 => 90,
        < 120 => 110,
        < 150 => 135,
        _ => 160
    };

    /// <summary>Runs frame updates only while a loaded, visible heart has a valid reading.</summary>
    private void UpdatePlayback()
    {
        var shouldPlay = targetBpm > 0 && IsVisible && IsLoaded;
        if (shouldPlay == playing) return;

        playing = shouldPlay;
        if (playing)
        {
            currentBpm = targetBpm;
            lastFrame = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += RenderBeat;
        }
        else
        {
            CompositionTarget.Rendering -= RenderBeat;
            phase = 0;
            scale.ScaleX = scale.ScaleY = 1;
        }
    }

    /// <summary>Eases the tempo toward the latest reading while preserving the animation phase.</summary>
    /// <param name="sender">The WPF rendering source.</param>
    /// <param name="args">The rendering event.</param>
    private void RenderBeat(object sender, EventArgs args)
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = Stopwatch.GetElapsedTime(lastFrame, now).TotalSeconds;
        lastFrame = now;

        // Integrate exponential tempo easing over elapsed time so dropped frames do not reset the beat.
        const double smoothingSeconds = 0.75;
        var blend = 1 - Math.Exp(-seconds / smoothingSeconds);
        var difference = currentBpm - targetBpm;
        phase = (phase + (targetBpm * seconds + difference * smoothingSeconds * blend) / 60) % 1;
        currentBpm -= difference * blend;
        scale.ScaleX = scale.ScaleY = 1 + 0.09 * (1 - Math.Cos(2 * Math.PI * phase));
    }
}
