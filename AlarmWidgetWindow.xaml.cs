using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class AlarmWidgetWindow : Window
{
    private readonly DispatcherTimer _clockTimer;
    private readonly AlarmSchedulerService _scheduler;
    private readonly List<AlarmRingingWindow> _ringingWindows = [];
    private AlarmStoreData _storeData = new();
    private bool _isLoaded;

    public ObservableCollection<AlarmListItem> AlarmItems { get; } = [];

    public AlarmWidgetWindow()
    {
        InitializeComponent();
        DataContext = this;

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => UpdateStatus();

        _scheduler = new AlarmSchedulerService(() => _storeData.Alarms);
        _scheduler.AlarmTriggered += Scheduler_AlarmTriggered;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _storeData = AlarmStore.Load();
        Topmost = _storeData.Topmost;
        TopmostMenuItem.IsChecked = _storeData.Topmost;
        LockMenuItem.IsChecked = _storeData.IsLocked;
        ResizeMenuItem.IsChecked = _storeData.IsResizable;
        ApplyResizeMode();
        if (_storeData.WindowWidth.HasValue && _storeData.WindowHeight.HasValue)
        {
            Width = _storeData.WindowWidth.Value;
            Height = _storeData.WindowHeight.Value;
        }
        RestorePosition();
        RefreshAlarmItems();
        UpdateStatus();
        _clockTimer.Start();
        _scheduler.Start();
        _isLoaded = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _clockTimer.Stop();
        _scheduler.Stop();
        StopAllRinging();
        SaveStore();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_storeData.IsLocked)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            WindowSnapService.SnapToScreen(this);
            SaveStore();
        }
    }

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _storeData.Topmost = TopmostMenuItem.IsChecked;
        Topmost = _storeData.Topmost;
        SaveStore();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _storeData.IsLocked = LockMenuItem.IsChecked;
        SaveStore();
    }

    private void ResizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _storeData.IsResizable = ResizeMenuItem.IsChecked;
        ApplyResizeMode();
        SaveStore();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded || !_storeData.IsResizable)
        {
            return;
        }

        WindowSnapService.SnapResize(this);
        SaveStore();
    }

    private void AddAlarmMenuItem_Click(object sender, RoutedEventArgs e)
    {
        AddAlarm();
    }

    private void AddAlarmButton_Click(object sender, RoutedEventArgs e)
    {
        AddAlarm();
    }

    private void StopAllRingingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        StopAllRinging();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void EnabledCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: AlarmInfo alarm } checkBox)
        {
            alarm.IsEnabled = checkBox.IsChecked == true;
            if (alarm.IsEnabled)
            {
                alarm.LastTriggeredDate = null;
            }

            SaveAndRefresh();
        }
    }

    private void EditAlarmButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AlarmInfo alarm })
        {
            EditAlarm(alarm);
        }
    }

    private void DeleteAlarmButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AlarmInfo alarm })
        {
            _storeData.Alarms.Remove(alarm);
            SaveAndRefresh();
        }
    }

    private void AddAlarm()
    {
        AlarmEditDialog dialog = new(null, _storeData.DefaultSnoozeMinutes)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _storeData.Alarms.Add(dialog.Alarm);
            _storeData.DefaultSnoozeMinutes = dialog.Alarm.SnoozeMinutes;
            SaveAndRefresh();
        }
    }

    private void EditAlarm(AlarmInfo alarm)
    {
        AlarmEditDialog dialog = new(alarm, _storeData.DefaultSnoozeMinutes)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        int index = _storeData.Alarms.FindIndex(item => item.Id == alarm.Id);
        if (index >= 0)
        {
            _storeData.Alarms[index] = dialog.Alarm;
            _storeData.DefaultSnoozeMinutes = dialog.Alarm.SnoozeMinutes;
            SaveAndRefresh();
        }
    }

    private void Scheduler_AlarmTriggered(object? sender, AlarmInfo alarm)
    {
        AlarmRingingWindow ringingWindow = new(alarm)
        {
            Owner = this
        };
        ringingWindow.StopRequested += RingingWindow_StopRequested;
        ringingWindow.SnoozeRequested += RingingWindow_SnoozeRequested;
        ringingWindow.Closed += (_, _) => _ringingWindows.Remove(ringingWindow);
        _ringingWindows.Add(ringingWindow);

        if (alarm.IsTemporarySnooze)
        {
            _storeData.Alarms.Remove(alarm);
        }

        SaveAndRefresh();
        ringingWindow.Show();
    }

    private void RingingWindow_StopRequested(object? sender, AlarmInfo alarm)
    {
        SaveAndRefresh();
    }

    private void RingingWindow_SnoozeRequested(object? sender, AlarmInfo alarm)
    {
        DateTime snoozeTime = DateTime.Now.AddMinutes(Math.Max(1, alarm.SnoozeMinutes));
        _storeData.Alarms.Add(new AlarmInfo
        {
            Time = new TimeSpan(snoozeTime.Hour, snoozeTime.Minute, 0),
            Label = $"{alarm.Label} Snooze",
            IsEnabled = true,
            SnoozeMinutes = alarm.SnoozeMinutes,
            CreatedAt = DateTime.Now,
            IsTemporarySnooze = true
        });
        SaveAndRefresh();
    }

    private void StopAllRinging()
    {
        foreach (AlarmRingingWindow window in _ringingWindows.ToList())
        {
            window.StopRinging();
        }

        _ringingWindows.Clear();
    }

    private void RefreshAlarmItems()
    {
        AlarmItems.Clear();
        foreach (AlarmInfo alarm in _storeData.Alarms
                     .Where(alarm => !alarm.IsTemporarySnooze)
                     .OrderBy(alarm => alarm.Time))
        {
            AlarmItems.Add(new AlarmListItem(alarm));
        }

        EmptyText.Visibility = AlarmItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AlarmsListBox.Visibility = AlarmItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateStatus()
    {
        CurrentTimeText.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        DateTime? nextAlarm = _scheduler.GetNextAlarmTime();
        if (nextAlarm.HasValue)
        {
            string prefix = nextAlarm.Value.Date == DateTime.Today
                ? "Today"
                : nextAlarm.Value.Date == DateTime.Today.AddDays(1)
                    ? "Tomorrow"
                    : nextAlarm.Value.ToString("ddd", CultureInfo.InvariantCulture);
            NextAlarmText.Text = $"Next: {prefix} {nextAlarm.Value:HH:mm}";
        }
        else
        {
            NextAlarmText.Text = "No active alarms";
        }
    }

    private void SaveAndRefresh()
    {
        SaveStore();
        RefreshAlarmItems();
        UpdateStatus();
    }

    private void RestorePosition()
    {
        if (_storeData.WindowLeft.HasValue && _storeData.WindowTop.HasValue)
        {
            Left = _storeData.WindowLeft.Value;
            Top = _storeData.WindowTop.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveStore()
    {
        _storeData.WindowLeft = Left;
        _storeData.WindowTop = Top;
        _storeData.WindowWidth = Width;
        _storeData.WindowHeight = Height;
        _storeData.Topmost = Topmost;
        _storeData.IsLocked = LockMenuItem.IsChecked;
        _storeData.IsResizable = ResizeMenuItem.IsChecked;
        AlarmStore.Save(_storeData);
    }

    private void ApplyResizeMode()
    {
        MinWidth = 320;
        MinHeight = 320;
        ResizeMode = _storeData.IsResizable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
    }

    public sealed class AlarmListItem
    {
        public AlarmInfo Alarm { get; }

        public AlarmListItem(AlarmInfo alarm)
        {
            Alarm = alarm;
        }

        public string TimeText => Alarm.Time.ToString(@"hh\:mm");
        public string Label => string.IsNullOrWhiteSpace(Alarm.Label) ? "Alarm" : Alarm.Label;
        public bool IsEnabled => Alarm.IsEnabled;
        public string RepeatText => Alarm.RepeatDays.Count == 0
            ? "Once"
            : string.Join(" ", Alarm.RepeatDays.Select(GetDayText));

        private static string GetDayText(DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Monday => "Mon",
                DayOfWeek.Tuesday => "Tue",
                DayOfWeek.Wednesday => "Wed",
                DayOfWeek.Thursday => "Thu",
                DayOfWeek.Friday => "Fri",
                DayOfWeek.Saturday => "Sat",
                DayOfWeek.Sunday => "Sun",
                _ => string.Empty
            };
        }
    }
}
