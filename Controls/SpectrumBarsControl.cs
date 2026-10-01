using System.Windows;
using System.Windows.Media;

namespace TimeWidget.Controls;

public sealed class SpectrumBarsControl : FrameworkElement
{
    public static readonly DependencyProperty HeightMultiplierProperty =
        DependencyProperty.Register(
            nameof(HeightMultiplier),
            typeof(double),
            typeof(SpectrumBarsControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender,
                null, (_, value) => double.IsFinite((double)value) ? Math.Clamp((double)value, 0.2, 5.0) : 1.0));

    public double HeightMultiplier
    {
        get => (double)GetValue(HeightMultiplierProperty);
        set => SetValue(HeightMultiplierProperty, value);
    }

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(
            nameof(Values),
            typeof(double[]),
            typeof(SpectrumBarsControl),
            new FrameworkPropertyMetadata(Array.Empty<double>(), OnValuesChanged));

    public static readonly DependencyProperty BarBrushProperty =
        DependencyProperty.Register(
            nameof(BarBrush),
            typeof(Brush),
            typeof(SpectrumBarsControl),
            new FrameworkPropertyMetadata(
                new SolidColorBrush(Color.FromArgb(0xD8, 0xA7, 0xD8, 0xFF)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public double[] Values
    {
        get => (double[])GetValue(ValuesProperty);
        set
        {
            SetValue(ValuesProperty, value);
            InvalidateVisual();
        }
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        double[] values = Values;
        int count = values.Length == 0 ? 32 : values.Length;
        double gap = 3;
        double barWidth = Math.Max(2, (ActualWidth - gap * (count - 1)) / count);
        double radius = Math.Min(barWidth / 2, 3);

        for (int i = 0; i < count; i++)
        {
            double sample = i < values.Length && double.IsFinite(values[i]) ? values[i] : 0;
            double value = Math.Clamp(sample * HeightMultiplier, 0, 1);
            double barHeight = Math.Min(ActualHeight, Math.Max(2, ActualHeight * value));
            double x = i * (barWidth + gap);
            double y = ActualHeight - barHeight;
            drawingContext.DrawRoundedRectangle(BarBrush, null, new Rect(x, y, barWidth, barHeight), radius, radius);
        }
    }

    private static void OnValuesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((SpectrumBarsControl)dependencyObject).InvalidateVisual();
    }
}
