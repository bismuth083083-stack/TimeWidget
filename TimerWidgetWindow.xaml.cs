using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class TimerWidgetWindow : Window
{
    private readonly DispatcherTimer _uiTimer;
    private readonly Stopwatch _stopwatch = new();
    private readonly SolidColorBrush _normalBackground = new(Color.FromRgb(0x1F, 0x1E, 0x33));
    private readonly SolidColorBrush _accentBackground = new(Color.FromRgb(0x27, 0x31, 0x42));
    private WidgetSettings _settings = new();
    private TimerWidgetMode _mode = TimerWidgetMode.Stopwatch;
    private TimeSpan _timerDuration = TimeSpan.FromMinutes(25);
    private TimeSpan _remainingWhenPaused = TimeSpan.FromMinutes(25);
    private DateTime? _timerEndTime;
    private int _lapCount;
    private int _flashTicksRemaining;

    public ObservableCollection<LapRecord> LapRecords { get; } = [];

    public TimerWidgetWindow()
    {
        InitializeComponent();
        DataContext = this;

        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _uiTimer.Tick += (_, _) => UpdateUi();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        Topmost = _settings.TimerTopmost;
        TopmostMenuItem.IsChecked = _settings.TimerTopmost;
        LockMenuItem.IsChecked = _settings.TimerIsLocked;
        _mode = _settings.TimerMode == "Timer" ? TimerWidgetMode.Timer : TimerWidgetMode.Stopwatch;
        int minutes = _settings.TimerDefaultMinutes > 0 ? _settings.TimerDefaultMinutes : 25;
        SetTimerDuration(minutes);
        RestorePosition();
        ApplyMode();
        UpdateUi();
        _uiTimer.Start();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _uiTimer.Stop();
        SaveSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.TimerIsLocked)
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
        _settings.TimerTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.TimerTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.TimerIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void ResetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ResetCurrentMode();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void StopwatchModeButton_Click(object sender, RoutedEventArgs e)
    {
        _mode = TimerWidgetMode.Stopwatch;
        ApplyMode();
        SaveSettings();
    }

    private void TimerModeButton_Click(object sender, RoutedEventArgs e)
    {
        _mode = TimerWidgetMode.Timer;
        ApplyMode();
        SaveSettings();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == TimerWidgetMode.Stopwatch)
        {
            _stopwatch.Start();
            return;
        }

        StartCountdown();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == TimerWidgetMode.Stopwatch)
        {
            _stopwatch.Stop();
            return;
        }

        if (_timerEndTime.HasValue)
        {
            _remainingWhenPaused = GetRemainingTime();
            _timerEndTime = null;
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetCurrentMode();
    }

    private void LapButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode != TimerWidgetMode.Stopwatch)
        {
            return;
        }

        _lapCount++;
        LapRecords.Insert(0, new LapRecord
        {
            Number = _lapCount,
            Elapsed = _stopwatch.Elapsed
        });

        while (LapRecords.Count > 3)
        {
            LapRecords.RemoveAt(LapRecords.Count - 1);
        }
    }

    private void FiveMinutesButton_Click(object sender, RoutedEventArgs e)
    {
        SetTimerDuration(5);
        SaveSettings();
    }

    private void TwentyFiveMinutesButton_Click(object sender, RoutedEventArgs e)
    {
        SetTimerDuration(25);
        SaveSettings();
    }

    private void FortyFiveMinutesButton_Click(object sender, RoutedEventArgs e)
    {
        SetTimerDuration(45);
        SaveSettings();
    }

    private void StartCountdown()
    {
        if (_timerEndTime.HasValue)
        {
            return;
        }

        if (!_timerEndTime.HasValue
            && (_remainingWhenPaused <= TimeSpan.Zero || _remainingWhenPaused == _timerDuration))
        {
            ApplyMinutesFromInput();
        }

        if (_remainingWhenPaused <= TimeSpan.Zero)
        {
            return;
        }

        _timerEndTime = DateTime.Now + _remainingWhenPaused;
        RootCard.Background = _normalBackground;
        SaveSettings();
    }

    private void ResetCurrentMode()
    {
        if (_mode == TimerWidgetMode.Stopwatch)
        {
            _stopwatch.Reset();
            _lapCount = 0;
            LapRecords.Clear();
        }
        else
        {
            ApplyMinutesFromInput();
            _timerEndTime = null;
            RootCard.Background = _normalBackground;
        }

        UpdateUi();
        SaveSettings();
    }

    private void ApplyMode()
    {
        bool isStopwatch = _mode == TimerWidgetMode.Stopwatch;
        StopwatchPanel.Visibility = isStopwatch ? Visibility.Visible : Visibility.Collapsed;
        TimerPanel.Visibility = isStopwatch ? Visibility.Collapsed : Visibility.Visible;
        TimerShortcutPanel.Visibility = isStopwatch ? Visibility.Collapsed : Visibility.Visible;
        LapButton.Visibility = isStopwatch ? Visibility.Visible : Visibility.Collapsed;

        StopwatchModeButton.Foreground = isStopwatch ? FindBrush("AccentText") : FindBrush("PrimaryText");
        TimerModeButton.Foreground = isStopwatch ? FindBrush("PrimaryText") : FindBrush("AccentText");
    }

    private void UpdateUi()
    {
        StopwatchDisplayText.Text = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture);

        if (_mode == TimerWidgetMode.Timer)
        {
            TimeSpan remaining = GetRemainingTime();
            if (_timerEndTime.HasValue && remaining <= TimeSpan.Zero)
            {
                CompleteCountdown();
            }
            else
            {
                TimerDisplayText.Text = remaining.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
            }
        }

        if (_flashTicksRemaining > 0)
        {
            RootCard.Background = _flashTicksRemaining % 2 == 0 ? _accentBackground : _normalBackground;
            _flashTicksRemaining--;
        }
    }

    private void CompleteCountdown()
    {
        _timerEndTime = null;
        _remainingWhenPaused = TimeSpan.Zero;
        TimerDisplayText.Text = "Done";
        SystemSounds.Exclamation.Play();
        _flashTicksRemaining = 6;
    }

    private TimeSpan GetRemainingTime()
    {
        if (!_timerEndTime.HasValue)
        {
            return _remainingWhenPaused < TimeSpan.Zero ? TimeSpan.Zero : _remainingWhenPaused;
        }

        TimeSpan remaining = _timerEndTime.Value - DateTime.Now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private void SetTimerDuration(int minutes)
    {
        minutes = Math.Clamp(minutes, 1, 999);
        _timerDuration = TimeSpan.FromMinutes(minutes);
        _remainingWhenPaused = _timerDuration;
        _timerEndTime = null;
        MinutesTextBox.Text = minutes.ToString(CultureInfo.InvariantCulture);
        TimerDisplayText.Text = _timerDuration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        _settings.TimerDefaultMinutes = minutes;
    }

    private void ApplyMinutesFromInput()
    {
        if (int.TryParse(MinutesTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes))
        {
            SetTimerDuration(minutes);
        }
        else
        {
            SetTimerDuration(_settings.TimerDefaultMinutes > 0 ? _settings.TimerDefaultMinutes : 25);
        }
    }

    private void RestorePosition()
    {
        if (_settings.TimerLeft.HasValue && _settings.TimerTop.HasValue)
        {
            Left = _settings.TimerLeft.Value;
            Top = _settings.TimerTop.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.TimerLeft = Left;
            settings.TimerTop = Top;
            settings.TimerTopmost = Topmost;
            settings.TimerIsLocked = LockMenuItem.IsChecked;
            settings.TimerMode = _mode == TimerWidgetMode.Timer ? "Timer" : "Stopwatch";
            settings.TimerDefaultMinutes = _settings.TimerDefaultMinutes > 0 ? _settings.TimerDefaultMinutes : 25;
        });
    }

    private Brush FindBrush(string key)
    {
        return (Brush)FindResource(key);
    }

    private enum TimerWidgetMode
    {
        Stopwatch,
        Timer
    }
}
