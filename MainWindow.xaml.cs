using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly IFolderWidgetWindowFactory _folderWidgetWindowFactory = new FolderWidgetWindowFactory();
    private WidgetSettings _settings = new();
    private bool _isLoaded;

    public MainWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => UpdateClock();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();

        Topmost = _settings.Topmost;
        TopmostMenuItem.IsChecked = _settings.Topmost;
        LockMenuItem.IsChecked = _settings.IsLocked;
        ResizeMenuItem.IsChecked = _settings.IsResizable;
        ApplyResizeMode();

        if (_settings.Width.HasValue && _settings.Height.HasValue)
        {
            Width = _settings.Width.Value;
            Height = _settings.Height.Value;
        }

        if (_settings.Left.HasValue && _settings.Top.HasValue)
        {
            Left = _settings.Left.Value;
            Top = _settings.Top.Value;
            KeepWindowOnScreen();
        }

        UpdateClock();
        _timer.Start();
        _isLoaded = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.IsLocked)
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

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.Topmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.Topmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void ResizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsResizable = ResizeMenuItem.IsChecked;
        ApplyResizeMode();
        SaveSettings();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenFilesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        FolderWidgetWindow folderWidgetWindow = _folderWidgetWindowFactory.Create();
        folderWidgetWindow.Show();
    }

    private void OpenMediaMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MediaWidgetWindow mediaWidgetWindow = new();
        mediaWidgetWindow.Show();
    }

    private void OpenWeatherMenuItem_Click(object sender, RoutedEventArgs e)
    {
        WeatherWidgetWindow weatherWidgetWindow = new();
        weatherWidgetWindow.Show();
    }

    private void OpenAlarmTimerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        AlarmTimerWidgetWindow alarmTimerWidgetWindow = new();
        alarmTimerWidgetWindow.Show();
    }

    private void OpenAiSearchMenuItem_Click(object sender, RoutedEventArgs e)
    {
        AiSearchWidgetWindow aiSearchWidgetWindow = new();
        aiSearchWidgetWindow.Show();
    }

    private void OpenCalendarMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CalendarWidgetWindow calendarWidgetWindow = new();
        calendarWidgetWindow.Show();
    }

    private void OpenScheduleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ScheduleWidgetWindow scheduleWidgetWindow = new();
        scheduleWidgetWindow.Show();
    }

    private void OpenVpnMenuItem_Click(object sender, RoutedEventArgs e)
    {
        VpnWidgetWindow vpnWidgetWindow = new();
        vpnWidgetWindow.Show();
    }

    private void OpenQuickSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SettingsWidgetWindow settingsWidgetWindow = new();
        settingsWidgetWindow.Show();
    }

    private void OpenPerformanceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        PerformanceWidgetWindow performanceWidgetWindow = new();
        performanceWidgetWindow.Show();
    }

    private void OpenNetworkTrafficMenuItem_Click(object sender, RoutedEventArgs e)
    {
        NetworkTrafficWidgetWindow networkTrafficWidgetWindow = new();
        networkTrafficWidgetWindow.Show();
    }

    private void OpenAudioControlMenuItem_Click(object sender, RoutedEventArgs e)
    {
        AudioControlWidgetWindow audioControlWidgetWindow = new();
        audioControlWidgetWindow.Show();
    }

    private void OpenFinanceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        FinanceWidgetWindow financeWidgetWindow = new();
        financeWidgetWindow.Show();
    }

    private void UpdateClock()
    {
        DateTime now = DateTime.Now;
        string timeText = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        string weekday = now.ToString("dddd", CultureInfo.InvariantCulture);
        string datePrefix = $"{now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)} ";
        TimeText.Text = timeText;
        TimeText.HighlightStart = 0;
        TimeText.HighlightLength = 1;
        DateText.Text = $"{datePrefix}{weekday}";
        DateText.HighlightStart = datePrefix.Length;
        DateText.HighlightLength = Math.Min(4, weekday.Length);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded || !_settings.IsResizable)
        {
            return;
        }

        WindowSnapService.SnapResize(this);
        SaveSettings();
    }

    private void ApplyResizeMode()
    {
        MinWidth = 280;
        MinHeight = 140;
        ResizeMode = _settings.IsResizable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.Left = Left;
            settings.Top = Top;
            settings.Width = Width;
            settings.Height = Height;
            settings.Topmost = Topmost;
            settings.IsLocked = LockMenuItem.IsChecked;
            settings.IsResizable = ResizeMenuItem.IsChecked;
        });
    }

    private void KeepWindowOnScreen()
    {
        WindowSnapService.KeepWindowOnScreen(this);
    }
}
