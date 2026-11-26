using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TimeWidget.Services;

internal sealed class LiveWidgetBackdrop : IDisposable
{
    private readonly Window _window;
    private readonly Border _card;
    private readonly HwndSource _foreground;
    private readonly HwndSource _backdrop;
    private (int Width, int Height, int Radius) _shape;
    private DispatcherOperation? _pending;

    public LiveWidgetBackdrop(Window window, Border card)
    {
        _window = window;
        _card = card;
        _foreground = (HwndSource)PresentationSource.FromVisual(window)!;
        // DWM blur needs a non-layered HWND. The existing WPF widget remains layered above it.
        _backdrop = new HwndSource(new HwndSourceParameters("TimeWidget blur")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = 0x080000A0, // NOACTIVATE | TOOLWINDOW | TRANSPARENT
            Width = 1,
            Height = 1
        });
        try
        {
            _backdrop.CompositionTarget.BackgroundColor = Colors.Transparent;
            _backdrop.AddHook(BackdropMessage);
            var policy = new AccentPolicy { State = 3 }; // ACCENT_ENABLE_BLURBEHIND
            var attribute = new CompositionAttribute { Attribute = 19, Size = (nuint)Marshal.SizeOf<AccentPolicy>() };
            attribute.Data = Marshal.AllocHGlobal((int)attribute.Size);
            try
            {
                Marshal.StructureToPtr(policy, attribute.Data, false);
                if (!SetWindowCompositionAttribute(_backdrop.Handle, ref attribute))
                    throw new InvalidOperationException("Windows did not enable backdrop blur.");
            }
            finally { Marshal.FreeHGlobal(attribute.Data); }

            _foreground.AddHook(ForegroundMessage);
            _card.SizeChanged += CardSizeChanged;
            _window.IsVisibleChanged += VisibilityChanged;
            Update();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static IntPtr BackdropMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x84) return IntPtr.Zero;
        handled = true;
        return new IntPtr(-1); // HTTRANSPARENT
    }

    private IntPtr ForegroundMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x47 or 0x02E0) ScheduleUpdate(); // WINDOWPOSCHANGED / DPICHANGED
        return IntPtr.Zero;
    }

    private void CardSizeChanged(object sender, SizeChangedEventArgs e) => ScheduleUpdate();
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => ScheduleUpdate();

    private void ScheduleUpdate()
    {
        if (_pending?.Status == DispatcherOperationStatus.Pending) return;
        _pending = _window.Dispatcher.InvokeAsync(Update, DispatcherPriority.Render);
    }

    private void Update()
    {
        if (!_window.IsVisible || _window.WindowState == WindowState.Minimized || !_card.IsLoaded
            || _card.ActualWidth <= 0 || _card.ActualHeight <= 0)
        {
            ShowWindow(_backdrop.Handle, 0);
            return;
        }

        Point topLeft = _card.PointToScreen(new Point());
        Point bottomRight = _card.PointToScreen(new Point(_card.ActualWidth, _card.ActualHeight));
        int x = (int)Math.Round(topLeft.X);
        int y = (int)Math.Round(topLeft.Y);
        int width = Math.Max(1, (int)Math.Round(bottomRight.X) - x);
        int height = Math.Max(1, (int)Math.Round(bottomRight.Y) - y);
        var shape = (width, height, (int)Math.Round(
            2 * _card.CornerRadius.TopLeft * VisualTreeHelper.GetDpi(_card).DpiScaleX));
        if (_shape != shape)
        {
            IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, shape.Item3, shape.Item3);
            if (region != IntPtr.Zero)
            {
                if (SetWindowRgn(_backdrop.Handle, region, true) != 0) _shape = shape;
                else DeleteObject(region);
            }
        }

        SetWindowPos(_backdrop.Handle, _foreground.Handle, x, y, width, height, 0x0050); // SHOWWINDOW | NOACTIVATE
    }

    public void Dispose()
    {
        _pending?.Abort();
        _card.SizeChanged -= CardSizeChanged;
        _window.IsVisibleChanged -= VisibilityChanged;
        _foreground.RemoveHook(ForegroundMessage);
        _backdrop.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int State, Flags, Color, Animation; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionAttribute { public int Attribute; public IntPtr Data; public nuint Size; }

    [DllImport("user32.dll")]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionAttribute attribute);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
