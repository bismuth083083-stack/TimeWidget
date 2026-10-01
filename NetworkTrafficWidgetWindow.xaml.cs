using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class NetworkTrafficWidgetWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly NetworkTrafficService _trafficService = new();
    private WidgetSettings _settings = new();
    private bool _isRefreshing;
    private bool _isClosed;

    public NetworkTrafficWidgetWindow()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshTrafficAsync();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        Topmost = _settings.NetworkTrafficTopmost;
        TopmostMenuItem.IsChecked = _settings.NetworkTrafficTopmost;
        LockMenuItem.IsChecked = _settings.NetworkTrafficIsLocked;
        RestorePosition();

        await RefreshTrafficAsync();
        _refreshTimer.Start();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _isClosed = true;
        _refreshTimer.Stop();
        SaveSettings();
        _trafficService.Dispose();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.NetworkTrafficIsLocked || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
        WindowSnapService.SnapToScreen(this);
        SaveSettings();
    }

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.NetworkTrafficTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.NetworkTrafficTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.NetworkTrafficIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private async void ResetTotalsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _trafficService.ResetSessionTotals();
        await RefreshTrafficAsync();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task RefreshTrafficAsync()
    {
        if (_isClosed || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            NetworkTrafficSnapshot snapshot = await Task.Run(_trafficService.Sample);
            if (_isClosed)
            {
                return;
            }

            DownloadText.Text = FormatRate(snapshot.DownloadBytesPerSecond);
            UploadText.Text = FormatRate(snapshot.UploadBytesPerSecond);
            SessionTotalText.Text = $"Session  D {FormatBytes(snapshot.SessionReceivedBytes)}  U {FormatBytes(snapshot.SessionSentBytes)}";
            AdapterText.Text = snapshot.IsAvailable ? snapshot.AdapterName : "No active network";
            StatusText.Text = snapshot.IsAvailable ? "LIVE" : "OFFLINE";
            StatusDot.Opacity = snapshot.IsAvailable ? 1 : 0.3;
        }
        catch
        {
            if (!_isClosed)
            {
                DownloadText.Text = "--";
                UploadText.Text = "--";
                AdapterText.Text = "Unavailable";
                StatusText.Text = "OFFLINE";
                StatusDot.Opacity = 0.3;
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static string FormatRate(double bytesPerSecond)
    {
        return $"{FormatValue(bytesPerSecond)}B/s";
    }

    private static string FormatBytes(long bytes)
    {
        return $"{FormatValue(bytes)}B";
    }

    private static string FormatValue(double bytes)
    {
        string[] units = ["", "K", "M", "G", "T"];
        int unitIndex = 0;
        double value = Math.Max(0, bytes);
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        string format = value >= 100 || unitIndex == 0 ? "0" : value >= 10 ? "0.0" : "0.00";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + units[unitIndex];
    }

    private void RestorePosition()
    {
        if (_settings.NetworkTrafficLeft.HasValue && _settings.NetworkTrafficTop.HasValue)
        {
            Left = _settings.NetworkTrafficLeft.Value;
            Top = _settings.NetworkTrafficTop.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.NetworkTrafficLeft = Left;
            settings.NetworkTrafficTop = Top;
            settings.NetworkTrafficTopmost = Topmost;
            settings.NetworkTrafficIsLocked = LockMenuItem.IsChecked;
        });
    }
}
