using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimeWidget.Controls;

public sealed class HighlightedTextBlock : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(HighlightedTextBlock),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty HighlightStartProperty =
        DependencyProperty.Register(
            nameof(HighlightStart),
            typeof(int),
            typeof(HighlightedTextBlock),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightLengthProperty =
        DependencyProperty.Register(
            nameof(HighlightLength),
            typeof(int),
            typeof(HighlightedTextBlock),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightBrushProperty =
        DependencyProperty.Register(
            nameof(HighlightBrush),
            typeof(Brush),
            typeof(HighlightedTextBlock),
            new FrameworkPropertyMetadata(Brushes.Red, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public int HighlightStart
    {
        get => (int)GetValue(HighlightStartProperty);
        set => SetValue(HighlightStartProperty, value);
    }

    public int HighlightLength
    {
        get => (int)GetValue(HighlightLengthProperty);
        set => SetValue(HighlightLengthProperty, value);
    }

    public Brush HighlightBrush
    {
        get => (Brush)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        FormattedText formattedText = CreateFormattedText(Text, Foreground);
        return new Size(formattedText.WidthIncludingTrailingWhitespace, formattedText.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        string text = Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        FormattedText fullText = CreateFormattedText(text, Foreground);
        double y = Math.Max(0, (ActualHeight - fullText.Height) / 2);
        drawingContext.DrawText(fullText, new Point(0, y));

        int start = Math.Clamp(HighlightStart, 0, text.Length);
        int length = Math.Clamp(HighlightLength, 0, text.Length - start);
        if (length == 0)
        {
            return;
        }

        string before = text[..start];
        string highlighted = text.Substring(start, length);
        double x = before.Length == 0
            ? 0
            : CreateFormattedText(before, Foreground).WidthIncludingTrailingWhitespace;

        drawingContext.DrawText(CreateFormattedText(highlighted, HighlightBrush), new Point(x, y));
    }

    private FormattedText CreateFormattedText(string text, Brush brush)
    {
        Typeface typeface = new(FontFamily, FontStyle, FontWeight, FontStretch);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return new FormattedText(
            text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            FlowDirection,
            typeface,
            FontSize,
            brush,
            pixelsPerDip);
    }
}
