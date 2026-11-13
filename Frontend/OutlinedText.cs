// Draws crisp, white-outlined text over the transparent floating window.
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DokiDoki.Frontend;

/// <summary>Renders the text as a filled glyph geometry with a solid white outline.</summary>
public sealed class OutlinedText : Control
{
    private const double OutlinePadding = 2;
    private static readonly Pen Outline = new(Brushes.White, 3) { LineJoin = PenLineJoin.Round };

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(OutlinedText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure |
            FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Measures the glyphs and reserves room for their outline.</summary>
    /// <param name="constraint">Available layout size.</param>
    /// <returns>The size of the formatted text including outline padding.</returns>
    protected override Size MeasureOverride(Size constraint)
    {
        var text = FormatText();
        return new Size(text.WidthIncludingTrailingWhitespace + OutlinePadding * 2,
            text.Height + OutlinePadding * 2);
    }

    /// <summary>Paints the white outline and then the colored glyph interior.</summary>
    /// <param name="drawingContext">The WPF drawing target.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var geometry = FormatText().BuildGeometry(new Point(OutlinePadding, OutlinePadding));
        drawingContext.DrawGeometry(null, Outline, geometry);
        drawingContext.DrawGeometry(Foreground, null, geometry);
    }

    /// <summary>Formats the full label with the current font and display scale.</summary>
    /// <returns>Text ready to measure or convert into glyph geometry.</returns>
    private FormattedText FormatText()
    {
        // Use the same natural width for measurement and drawing. Layout rounding can
        // otherwise make the arranged width slightly narrower and trigger ellipsis.
        return new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
