using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TimeWidget.Controls;

public sealed class WeatherIconControl : FrameworkElement
{
    public static readonly DependencyProperty WeatherTextProperty =
        DependencyProperty.Register(
            nameof(WeatherText),
            typeof(string),
            typeof(WeatherIconControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string WeatherText
    {
        get => (string)GetValue(WeatherTextProperty);
        set => SetValue(WeatherTextProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        drawingContext.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        string text = WeatherText.ToLower(CultureInfo.InvariantCulture);

        if (text.Contains("thunder"))
        {
            DrawCloud(drawingContext, size, true);
            DrawRain(drawingContext, size, 2);
            DrawLightning(drawingContext, size);
        }
        else if (text.Contains("rain") || text.Contains("drizzle"))
        {
            DrawCloud(drawingContext, size, false);
            DrawRain(drawingContext, size, 3);
        }
        else if (text.Contains("snow"))
        {
            DrawCloud(drawingContext, size, false);
            DrawSnow(drawingContext, size);
        }
        else if (text.Contains("fog"))
        {
            DrawCloud(drawingContext, size, false);
            DrawFog(drawingContext, size);
        }
        else if (text.Contains("partly"))
        {
            DrawSun(drawingContext, size, 0.34, 0.36, 0.26);
            DrawCloud(drawingContext, size, false);
        }
        else if (text.Contains("cloud"))
        {
            DrawCloud(drawingContext, size, false);
        }
        else
        {
            DrawSun(drawingContext, size, 0.5, 0.5, 0.28);
        }

        drawingContext.Pop();
    }

    private static void DrawSun(DrawingContext dc, double size, double cx, double cy, double radiusRatio)
    {
        Brush sunBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x49));
        Pen rayPen = new(new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x6B)), Math.Max(1.4, size * 0.055))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        Point center = new(size * cx, size * cy);
        double radius = size * radiusRatio;
        for (int i = 0; i < 8; i++)
        {
            double angle = Math.PI * 2 * i / 8;
            Point start = new(center.X + Math.Cos(angle) * radius * 1.24, center.Y + Math.Sin(angle) * radius * 1.24);
            Point end = new(center.X + Math.Cos(angle) * radius * 1.58, center.Y + Math.Sin(angle) * radius * 1.58);
            dc.DrawLine(rayPen, start, end);
        }

        dc.DrawEllipse(sunBrush, null, center, radius, radius);
    }

    private static void DrawCloud(DrawingContext dc, double size, bool storm)
    {
        Brush cloudBrush = new LinearGradientBrush(
            storm ? Color.FromRgb(0x9A, 0xA5, 0xB8) : Color.FromRgb(0xDE, 0xEB, 0xF7),
            storm ? Color.FromRgb(0x67, 0x72, 0x86) : Color.FromRgb(0xA9, 0xBD, 0xD0),
            90);

        dc.DrawEllipse(cloudBrush, null, new Point(size * 0.40, size * 0.50), size * 0.20, size * 0.17);
        dc.DrawEllipse(cloudBrush, null, new Point(size * 0.55, size * 0.43), size * 0.24, size * 0.22);
        dc.DrawEllipse(cloudBrush, null, new Point(size * 0.69, size * 0.53), size * 0.20, size * 0.16);
        dc.DrawRoundedRectangle(cloudBrush, null, new Rect(size * 0.24, size * 0.48, size * 0.58, size * 0.22), size * 0.11, size * 0.11);
    }

    private static void DrawRain(DrawingContext dc, double size, int count)
    {
        Pen rainPen = new(new SolidColorBrush(Color.FromRgb(0x6E, 0xCB, 0xFF)), Math.Max(1.5, size * 0.055))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        for (int i = 0; i < count; i++)
        {
            double x = size * (0.36 + i * 0.14);
            dc.DrawLine(rainPen, new Point(x, size * 0.73), new Point(x - size * 0.05, size * 0.91));
        }
    }

    private static void DrawSnow(DrawingContext dc, double size)
    {
        Pen snowPen = new(new SolidColorBrush(Color.FromRgb(0xD9, 0xF5, 0xFF)), Math.Max(1.2, size * 0.038))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        for (int i = 0; i < 2; i++)
        {
            Point center = new(size * (0.43 + i * 0.18), size * 0.82);
            double r = size * 0.065;
            dc.DrawLine(snowPen, new Point(center.X - r, center.Y), new Point(center.X + r, center.Y));
            dc.DrawLine(snowPen, new Point(center.X, center.Y - r), new Point(center.X, center.Y + r));
            dc.DrawLine(snowPen, new Point(center.X - r * 0.7, center.Y - r * 0.7), new Point(center.X + r * 0.7, center.Y + r * 0.7));
            dc.DrawLine(snowPen, new Point(center.X - r * 0.7, center.Y + r * 0.7), new Point(center.X + r * 0.7, center.Y - r * 0.7));
        }
    }

    private static void DrawFog(DrawingContext dc, double size)
    {
        Pen fogPen = new(new SolidColorBrush(Color.FromArgb(0xDD, 0xC5, 0xD0, 0xDF)), Math.Max(1.5, size * 0.045))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        dc.DrawLine(fogPen, new Point(size * 0.24, size * 0.76), new Point(size * 0.78, size * 0.76));
        dc.DrawLine(fogPen, new Point(size * 0.32, size * 0.88), new Point(size * 0.70, size * 0.88));
    }

    private static void DrawLightning(DrawingContext dc, double size)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(size * 0.57, size * 0.66), true, true);
            context.LineTo(new Point(size * 0.45, size * 0.86), true, false);
            context.LineTo(new Point(size * 0.56, size * 0.84), true, false);
            context.LineTo(new Point(size * 0.49, size * 1.02), true, false);
            context.LineTo(new Point(size * 0.72, size * 0.75), true, false);
            context.LineTo(new Point(size * 0.60, size * 0.77), true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A)), null, geometry);
    }
}
