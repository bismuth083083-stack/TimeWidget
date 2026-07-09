using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class AlarmTimerWidgetWindow : Window
{
    private readonly DispatcherTimer _uiTimer;
    private readonly Stopwatch _stopwatch = new();
    private readonly AlarmSchedulerService _scheduler;
    private readonly List<AlarmRingingWindow> _ringingWindows = [];
    private AlarmStoreData _alarmStore = new();
    private WidgetSettings _settings = new();
    private TimeSpan _timerDuration = TimeSpan.FromMinutes(25);
    private TimeSpan _remainingWhenPaused = TimeSpan.FromMinutes(25);
    private DateTime? _timerEndTime;

    public AlarmTimerWidgetWindow()
    {
        InitializeComponent();

        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _uiTimer.Tick += (_, _) => UpdateUi();

        _scheduler = new AlarmSchedulerService(() => _alarmStore.Alarms);
        _scheduler.AlarmTriggered += Scheduler_AlarmTriggered;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _alarmStore = AlarmStore.Load();
        _settings = SettingsStore.Load();
        Topmost = _alarmStore.Topmost;
        TopmostMenuItem.IsChecked = _alarmStore.Topmost;
        LockMenuItem.IsChecked = _alarmStore.IsLocked;
        Width = 360;
        Height = 160;

        int minutes = _settings.TimerDefaultMinutes > 0 ? _settings.TimerDefaultMinutes : 25;
        SetTimerDuration(minutes);
        RestorePosition();
        UpdateUi();
        _uiTimer.Start();
        _scheduler.Start();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _uiTimer.Stop();
        _scheduler.Stop();
        StopAllRinging();
        SaveAll();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_alarmStore.IsLocked)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            WindowSnapService.SnapToScreen(this);
            SaveAll();
        }
    }

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _alarmStore.Topmost = TopmostMenuItem.IsChecked;
        Topmost = _alarmStore.Topmost;
        SaveAll();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _alarmStore.IsLocked = LockMenuItem.IsChecked;
        SaveAll();
    }

    private void AddAlarmMenuItem_Click(object sender, RoutedEventArgs e) => AddAlarm();
    private void AddAlarmButton_Click(object sender, RoutedEventArgs e) => AddAlarm();
    private void StopAllRingingMenuItem_Click(object sender, RoutedEventArgs e) => StopAllRinging();
    private void StopAllRingingButton_Click(object sender, RoutedEventArgs e) => StopAllRinging();

    private void ResetAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ResetTimer();
        ResetStopwatch();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TimerStartPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_timerEndTime.HasValue)
        {
            _remainingWhenPaused = GetRemainingTime();
            _timerEndTime = null;
            TimerStartPauseButton.Content = "Start";
            return;
        }

        if (_remainingWhenPaused <= TimeSpan.Zero)
        {
            SetTimerDuration(_settings.TimerDefaultMinutes > 0 ? _settings.TimerDefaultMinutes : 25);
        }

        _timerEndTime = DateTime.Now + _remainingWhenPaused;
        TimerStartPauseButton.Content = "Pause";
    }

    private void TimerResetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetTimer();
    }

    private void AlarmCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            ShowAlarmOverview();
            e.Handled = true;
        }
    }

    private void TimerCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            ShowTimerDurationDialog();
            e.Handled = true;
        }
    }

    private void StopwatchStartPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stopwatch.IsRunning)
        {
            _stopwatch.Stop();
            StopwatchStartPauseButton.Content = "Start";
        }
        else
        {
            _stopwatch.Start();
            StopwatchStartPauseButton.Content = "Pause";
        }
    }

    private void StopwatchResetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetStopwatch();
    }

    private void AddAlarm()
    {
        AlarmEditDialog dialog = new(null, _alarmStore.DefaultSnoozeMinutes)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _alarmStore.Alarms.Add(dialog.Alarm);
            _alarmStore.DefaultSnoozeMinutes = dialog.Alarm.SnoozeMinutes;
            SaveAll();
            UpdateAlarmInfo();
        }
    }

    private void Scheduler_AlarmTriggered(object? sender, AlarmInfo alarm)
    {
        AlarmRingingWindow ringingWindow = new(alarm)
        {
            Owner = this
        };
        ringingWindow.StopRequested += (_, _) => SaveAll();
        ringingWindow.SnoozeRequested += RingingWindow_SnoozeRequested;
        ringingWindow.Closed += (_, _) => _ringingWindows.Remove(ringingWindow);
        _ringingWindows.Add(ringingWindow);

        if (alarm.IsTemporarySnooze)
        {
            _alarmStore.Alarms.Remove(alarm);
        }

        SaveAll();
        ringingWindow.Show();
    }

    private void RingingWindow_SnoozeRequested(object? sender, AlarmInfo alarm)
    {
        DateTime snoozeTime = DateTime.Now.AddMinutes(Math.Max(1, alarm.SnoozeMinutes));
        _alarmStore.Alarms.Add(new AlarmInfo
        {
            Time = new TimeSpan(snoozeTime.Hour, snoozeTime.Minute, 0),
            Label = $"{alarm.Label} Snooze",
            IsEnabled = true,
            SnoozeMinutes = alarm.SnoozeMinutes,
            CreatedAt = DateTime.Now,
            IsTemporarySnooze = true
        });
        SaveAll();
    }

    private void StopAllRinging()
    {
        foreach (AlarmRingingWindow window in _ringingWindows.ToList())
        {
            window.StopRinging();
        }

        _ringingWindows.Clear();
    }

    private void UpdateUi()
    {
        UpdateAlarmInfo();
        UpdateTimerInfo();
        StopwatchDisplayText.Text = _stopwatch.Elapsed.ToString(@"mm\:ss\.ff", CultureInfo.InvariantCulture);
    }

    private void UpdateAlarmInfo()
    {
        DateTime? nextAlarm = _scheduler.GetNextAlarmTime();
        if (nextAlarm.HasValue)
        {
            AlarmMainText.Text = nextAlarm.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
            AlarmSubText.Text = nextAlarm.Value.Date == DateTime.Today
                ? "Today"
                : nextAlarm.Value.Date == DateTime.Today.AddDays(1)
                    ? "Tomorrow"
                    : nextAlarm.Value.ToString("ddd", CultureInfo.InvariantCulture);
        }
        else
        {
            AlarmMainText.Text = "--:--";
            AlarmSubText.Text = "No active";
        }
    }

    private void UpdateTimerInfo()
    {
        TimeSpan remaining = GetRemainingTime();
        if (_timerEndTime.HasValue && remaining <= TimeSpan.Zero)
        {
            _timerEndTime = null;
            _remainingWhenPaused = TimeSpan.Zero;
            TimerDisplayText.Text = "Done";
            TimerStartPauseButton.Content = "Start";
            SystemSounds.Exclamation.Play();
            return;
        }

        TimerDisplayText.Text = remaining.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void ResetTimer()
    {
        SetTimerDuration(_settings.TimerDefaultMinutes > 0 ? _settings.TimerDefaultMinutes : 25);
        TimerStartPauseButton.Content = "Start";
    }

    private void ResetStopwatch()
    {
        _stopwatch.Reset();
        StopwatchStartPauseButton.Content = "Start";
        StopwatchDisplayText.Text = "00:00.00";
    }

    private void ShowAlarmOverview()
    {
        Window dialog = CreateDialogWindow("Alarms", 300, 300);
        Grid root = CreateDialogRoot();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock title = CreateDialogTitle("Alarms");
        root.Children.Add(title);

        ListBox listBox = new()
        {
            Margin = new Thickness(0, 12, 0, 12),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = FindBrush("PrimaryText"),
            FontFamily = new FontFamily("MiSans"),
            FontWeight = FontWeights.Bold
        };
        Grid.SetRow(listBox, 1);

        List<AlarmInfo> alarms = _alarmStore.Alarms
            .Where(alarm => !alarm.IsTemporarySnooze)
            .OrderBy(alarm => alarm.Time)
            .ToList();
        listBox.ItemsSource = alarms.Count == 0
            ? new[] { "No alarms yet" }
            : alarms.Select(alarm => $"{alarm.Time:hh\\:mm}  {(string.IsNullOrWhiteSpace(alarm.Label) ? "Alarm" : alarm.Label)}  {(alarm.IsEnabled ? "On" : "Off")}");
        root.Children.Add(listBox);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button addButton = CreateDialogButton("Add");
        addButton.Click += (_, _) =>
        {
            dialog.Close();
            AddAlarm();
        };
        Button closeButton = CreateDialogButton("Close");
        closeButton.Margin = new Thickness(8, 0, 0, 0);
        closeButton.Click += (_, _) => dialog.Close();
        buttons.Children.Add(addButton);
        buttons.Children.Add(closeButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = CreateDialogFrame(root);
        dialog.ShowDialog();
    }

    private void ShowTimerDurationDialog()
    {
        Window dialog = CreateDialogWindow("Timer", 260, 170);
        Grid root = CreateDialogRoot();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock title = CreateDialogTitle("Timer minutes");
        root.Children.Add(title);

        TextBox minutesBox = CreateDialogTextBox(Math.Max(1, (int)_timerDuration.TotalMinutes).ToString(CultureInfo.InvariantCulture));
        Grid.SetRow(minutesBox, 1);
        root.Children.Add(minutesBox);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button cancelButton = CreateDialogButton("Cancel");
        cancelButton.Click += (_, _) => dialog.Close();
        Button saveButton = CreateDialogButton("Save");
        saveButton.Margin = new Thickness(8, 0, 0, 0);
        saveButton.Click += (_, _) =>
        {
            if (int.TryParse(minutesBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes))
            {
                SetTimerDuration(minutes);
                TimerStartPauseButton.Content = "Start";
                SaveAll();
                dialog.Close();
            }
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = CreateDialogFrame(root);
        dialog.ShowDialog();
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
        TimerDisplayText.Text = _timerDuration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        _settings.TimerDefaultMinutes = minutes;
    }

    private void RestorePosition()
    {
        if (_alarmStore.WindowLeft.HasValue && _alarmStore.WindowTop.HasValue)
        {
            Left = _alarmStore.WindowLeft.Value;
            Top = _alarmStore.WindowTop.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveAll()
    {
        _alarmStore.WindowLeft = Left;
        _alarmStore.WindowTop = Top;
        _alarmStore.WindowWidth = Width;
        _alarmStore.WindowHeight = Height;
        _alarmStore.Topmost = Topmost;
        _alarmStore.IsLocked = LockMenuItem.IsChecked;
        _alarmStore.IsResizable = false;
        AlarmStore.Save(_alarmStore);

        SettingsStore.Update(settings =>
        {
            settings.TimerDefaultMinutes = _settings.TimerDefaultMinutes > 0 ? _settings.TimerDefaultMinutes : 25;
        });
    }

    private Window CreateDialogWindow(string title, double width, double height)
    {
        return new Window
        {
            Title = title,
            Width = width,
            Height = height,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false
        };
    }

    private Grid CreateDialogRoot()
    {
        return new Grid
        {
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            ClipToBounds = true
        };
    }

    private Border CreateDialogFrame(Grid root)
    {
        return new Border
        {
            Margin = new Thickness(0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(22),
            Background = FindBrush("MainBackground"),
            BorderBrush = FindBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Child = root,
            SnapsToDevicePixels = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 2,
                Direction = 270,
                Opacity = 0.18,
                Color = Colors.Black
            }
        };
    }

    private TextBlock CreateDialogTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("MiSans"),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("PrimaryText")
        };
    }

    private Button CreateDialogButton(string text)
    {
        return new Button
        {
            Content = text,
            MinWidth = 64,
            Height = 30,
            Background = FindBrush("SecondaryBackground"),
            BorderBrush = FindBrush("BorderBrush"),
            Foreground = FindBrush("PrimaryText"),
            FontFamily = new FontFamily("MiSans"),
            FontWeight = FontWeights.Bold
        };
    }

    private TextBox CreateDialogTextBox(string text)
    {
        return new TextBox
        {
            Text = text,
            Width = 90,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = FindBrush("SecondaryBackground"),
            BorderBrush = FindBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Foreground = FindBrush("PrimaryText"),
            CaretBrush = FindBrush("PrimaryText"),
            FontFamily = new FontFamily("MiSans"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(8, 3, 8, 3),
            FocusVisualStyle = null,
            Template = CreateRoundedDialogTextBoxTemplate()
        };
    }

    private ControlTemplate CreateRoundedDialogTextBoxTemplate()
    {
        FrameworkElementFactory contentHost = new(typeof(ScrollViewer), "PART_ContentHost");
        contentHost.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));

        FrameworkElementFactory border = new(typeof(Border), "TextBoxBorder");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.AppendChild(contentHost);

        ControlTemplate template = new(typeof(TextBox));
        template.VisualTree = border;
        Trigger focusedTrigger = new()
        {
            Property = IsKeyboardFocusedProperty,
            Value = true
        };
        focusedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, FindBrush("AccentText"), "TextBoxBorder"));
        template.Triggers.Add(focusedTrigger);
        return template;
    }

    private Brush FindBrush(string key)
    {
        return (Brush)FindResource(key);
    }
}
