using System.Windows;
using System.Windows.Media;

namespace TimeWidget.Controls;

public sealed class SpectrumBarsControl : FrameworkElement
{
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

        double[] values = Values.Length == 0 ? new double[32] : Values;
        double gap = 3;
        double barWidth = Math.Max(2, (ActualWidth - gap * (values.Length - 1)) / values.Length);
        double radius = Math.Min(barWidth / 2, 3);

        for (int i = 0; i < values.Length; i++)
        {
            double value = Math.Clamp(values[i], 0, 1);
            double barHeight = Math.Max(2, ActualHeight * value);
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
