using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class PerformanceWidgetWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly PerformanceMonitorService _monitorService = new();
    private WidgetSettings _settings = new();
    private PerformanceSnapshot _snapshot = new();
    private int _pageIndex;
    private bool _isRefreshing;

    public PerformanceWidgetWindow()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshSnapshotAsync();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        Topmost = _settings.PerformanceTopmost;
        TopmostMenuItem.IsChecked = _settings.PerformanceTopmost;
        LockMenuItem.IsChecked = _settings.PerformanceIsLocked;
        RestorePosition();
        RenderPage(false);
        await RefreshSnapshotAsync();
        _refreshTimer.Start();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _refreshTimer.Stop();
        SaveSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.PerformanceIsLocked)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            WindowSnapService.SnapToScreen(this);
            SaveSettings();
        }
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _pageIndex = e.Delta < 0
            ? (_pageIndex + 1) % 3
            : (_pageIndex + 2) % 3;
        RenderPage(true);
    }

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.PerformanceTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.PerformanceTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.PerformanceIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task RefreshSnapshotAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            _snapshot = await _monitorService.GetSnapshotAsync();
            RenderPage(false);
        }
        catch
        {
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void RenderPage(bool animate)
    {
        PageText.Text = $"{_pageIndex + 1}/3";
        switch (_pageIndex)
        {
            case 0:
                RenderHardwarePage("CPU", _snapshot.Cpu);
                break;
            case 1:
                RenderHardwarePage("GPU", _snapshot.Gpu);
                break;
            default:
                RenderMemoryPage();
                break;
        }

        if (animate && !IsMouseOver)
        {
            PageContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(160)));
        }
        else if (animate)
        {
            PageContent.Opacity = 1;
        }
    }

    private void RenderHardwarePage(string title, PerformanceMetricInfo metric)
    {
        TitleText.Text = title;
        MainLabelText.Text = "Usage";
        MainValueText.Text = FormatPercent(metric.UsagePercent);
        UsageProgressBar.Value = metric.UsagePercent ?? 0;
        FrequencyText.Text = metric.FrequencyMHz.HasValue ? $"{metric.FrequencyMHz.Value:0} MHz" : "--";
        TemperatureText.Text = metric.TemperatureC.HasValue ? $"{metric.TemperatureC.Value:0}°C" : "--";
        PowerText.Text = metric.PowerW.HasValue ? $"{metric.PowerW.Value:0.#} W" : "--";
        HintText.Text = title == "GPU" ? "NVIDIA data via nvidia-smi when available" : "Temperature and power may be unavailable";
    }

    private void RenderMemoryPage()
    {
        MemoryMetricInfo memory = _snapshot.Memory;
        TitleText.Text = "Memory";
        MainLabelText.Text = "Used";
        MainValueText.Text = FormatPercent(memory.UsagePercent);
        UsageProgressBar.Value = memory.UsagePercent ?? 0;
        FrequencyText.Text = memory.UsedGB.HasValue && memory.TotalGB.HasValue
            ? $"{memory.UsedGB.Value:0.0}/{memory.TotalGB.Value:0.0} GB"
            : "--";
        TemperatureText.Text = memory.AvailableGB.HasValue ? $"{memory.AvailableGB.Value:0.0} GB" : "--";
        PowerText.Text = "--";
        HintText.Text = "Used / total · available memory";
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue ? value.Value.ToString("0", CultureInfo.InvariantCulture) + "%" : "--";
    }

    private void RestorePosition()
    {
        if (_settings.PerformanceLeft.HasValue && _settings.PerformanceTop.HasValue)
        {
            Left = _settings.PerformanceLeft.Value;
            Top = _settings.PerformanceTop.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.PerformanceLeft = Left;
            settings.PerformanceTop = Top;
            settings.PerformanceTopmost = Topmost;
            settings.PerformanceIsLocked = LockMenuItem.IsChecked;
        });
    }
}
