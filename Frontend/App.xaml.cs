// WPF application entry point and shared help tooltip placement.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DokiDoki.Frontend;

/// <summary>Provides application lifetime and shared help behavior.</summary>
public partial class App : Application
{
    /// <summary>Anchors the tooltip's left edge to the lower right of the current pointer.</summary>
    /// <param name="sender">The help badge that raised the event.</param>
    /// <param name="args">The WPF tooltip opening event.</param>
    private void PlaceHelpTooltip(object sender, ToolTipEventArgs args)
    {
        if (sender is not Label { ToolTip: ToolTip tip } badge) return;

        var pointer = Mouse.GetPosition(badge);
        tip.PlacementTarget = badge;
        tip.Placement = PlacementMode.Custom;
        // Use an explicit top-left origin rather than the system's mouse placement alignment.
        tip.CustomPopupPlacementCallback = (_, _, _) =>
            new[] { new CustomPopupPlacement(new Point(pointer.X + 24, pointer.Y + 30), PopupPrimaryAxis.Vertical) };
    }
}
