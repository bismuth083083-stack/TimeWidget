using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TimeWidget.Controls;

public sealed class MarqueeTextBlock : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty TextBrushProperty =
        DependencyProperty.Register(
            nameof(TextBrush),
            typeof(Brush),
            typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(Brushes.White, OnVisualPropertyChanged));

    public static readonly DependencyProperty TextSizeProperty =
        DependencyProperty.Register(
            nameof(TextSize),
            typeof(double),
            typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(14d, OnVisualPropertyChanged));

    public static readonly DependencyProperty TextWeightProperty =
        DependencyProperty.Register(
            nameof(TextWeight),
            typeof(FontWeight),
            typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(FontWeights.Normal, OnVisualPropertyChanged));

    private readonly DispatcherTimer _timer;
    private readonly TranslateTransform _translateTransform = new();
    private TextBlock? _textBlock;
    private double _offset;
    private bool _isOverflowing;

    public MarqueeTextBlock()
    {
        ClipToBounds = true;
        _textBlock = new TextBlock
        {
            RenderTransform = _translateTransform,
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap
        };
        AddVisualChild(_textBlock);
        AddLogicalChild(_textBlock);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => UpdateScrollingState();
        Unloaded += (_, _) => _timer.Stop();
        SizeChanged += (_, _) => UpdateScrollingState();
        ApplyTextProperties();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public double TextSize
    {
        get => (double)GetValue(TextSizeProperty);
        set => SetValue(TextSizeProperty, value);
    }

    public FontWeight TextWeight
    {
        get => (FontWeight)GetValue(TextWeightProperty);
        set => SetValue(TextWeightProperty, value);
    }

    protected override int VisualChildrenCount => _textBlock is null ? 0 : 1;

    protected override Visual GetVisualChild(int index)
    {
        if (_textBlock is null || index != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _textBlock;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _textBlock?.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        return new Size(availableSize.Width, _textBlock?.DesiredSize.Height ?? 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _textBlock?.Arrange(new Rect(0, 0, _textBlock.DesiredSize.Width, finalSize.Height));
        UpdateScrollingState();
        return finalSize;
    }

    private void Tick()
    {
        if (!_isOverflowing || _textBlock is null)
        {
            return;
        }

        _offset -= 0.55;
        double textWidth = Math.Max(_textBlock.ActualWidth, _textBlock.DesiredSize.Width);
        double resetAt = -(textWidth + 28);
        if (_offset <= resetAt)
        {
            _offset = ActualWidth;
        }

        _translateTransform.X = _offset;
    }

    private void UpdateScrollingState()
    {
        if (_textBlock is null || ActualWidth <= 0)
        {
            return;
        }

        _textBlock.Measure(new Size(double.PositiveInfinity, ActualHeight));
        double textWidth = Math.Max(_textBlock.ActualWidth, _textBlock.DesiredSize.Width);
        _isOverflowing = textWidth > ActualWidth;
        if (_isOverflowing)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _offset = 0;
            _translateTransform.X = 0;
        }
    }

    private void ApplyTextProperties()
    {
        if (_textBlock is null)
        {
            return;
        }

        _textBlock.Text = Text;
        _textBlock.Foreground = TextBrush;
        _textBlock.FontSize = TextSize;
        _textBlock.FontWeight = TextWeight;
        _textBlock.FontFamily = FontFamily;
        InvalidateMeasure();
    }

    private static void OnVisualPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        MarqueeTextBlock marqueeTextBlock = (MarqueeTextBlock)dependencyObject;
        marqueeTextBlock.ApplyTextProperties();
        marqueeTextBlock._offset = 0;
        marqueeTextBlock._translateTransform.X = 0;
        marqueeTextBlock.Dispatcher.BeginInvoke(marqueeTextBlock.UpdateScrollingState);
    }
}
