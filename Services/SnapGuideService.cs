using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace TimeWidget.Services;

public static class SnapGuideService
{
    private static readonly DispatcherTimer HideTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(650)
    };

    private static Window? _verticalGuide;
    private static Window? _horizontalGuide;

    static SnapGuideService()
    {
        HideTimer.Tick += (_, _) =>
        {
            HideTimer.Stop();
            HideGuides();
        };
    }

    public static void ShowVertical(double x)
    {
        Window guide = GetOrCreateGuide(ref _verticalGuide);
        guide.Left = Math.Round(x);
        guide.Top = SystemParameters.VirtualScreenTop;
        guide.Width = 1;
        guide.Height = SystemParameters.VirtualScreenHeight;
        ShowGuide(guide);
    }

    public static void ShowHorizontal(double y)
    {
        Window guide = GetOrCreateGuide(ref _horizontalGuide);
        guide.Left = SystemParameters.VirtualScreenLeft;
        guide.Top = Math.Round(y);
        guide.Width = SystemParameters.VirtualScreenWidth;
        guide.Height = 1;
        ShowGuide(guide);
    }

    private static Window GetOrCreateGuide(ref Window? guide)
    {
        if (guide is not null)
        {
            return guide;
        }

        guide = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xA7, 0xD8, 0xFF)),
            Topmost = true,
            IsHitTestVisible = false,
            ShowActivated = false,
            Focusable = false
        };

        return guide;
    }

    private static void ShowGuide(Window guide)
    {
        if (!guide.IsVisible)
        {
            guide.Show();
        }

        HideTimer.Stop();
        HideTimer.Start();
    }

    private static void HideGuides()
    {
        _verticalGuide?.Hide();
        _horizontalGuide?.Hide();
    }
}
