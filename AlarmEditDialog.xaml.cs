using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TimeWidget.Models;

namespace TimeWidget;

public partial class AlarmEditDialog : Window
{
    private readonly AlarmInfo _alarm;

    public AlarmInfo Alarm => _alarm;

    public AlarmEditDialog(AlarmInfo? alarm = null, int defaultSnoozeMinutes = 5)
    {
        InitializeComponent();
        _alarm = alarm is null
            ? new AlarmInfo
            {
                Time = new TimeSpan(7, 30, 0),
                Label = "Morning",
                SnoozeMinutes = defaultSnoozeMinutes
            }
            : CloneAlarm(alarm);

        FillFields();
    }

    private void FillFields()
    {
        HourTextBox.Text = _alarm.Time.Hours.ToString("00");
        MinuteTextBox.Text = _alarm.Time.Minutes.ToString("00");
        LabelTextBox.Text = string.IsNullOrWhiteSpace(_alarm.Label) ? "Alarm" : _alarm.Label;
        SnoozeTextBox.Text = Math.Max(1, _alarm.SnoozeMinutes).ToString();

        MonCheckBox.IsChecked = _alarm.RepeatDays.Contains(DayOfWeek.Monday);
        TueCheckBox.IsChecked = _alarm.RepeatDays.Contains(DayOfWeek.Tuesday);
        WedCheckBox.IsChecked = _alarm.RepeatDays.Contains(DayOfWeek.Wednesday);
        ThuCheckBox.IsChecked = _alarm.RepeatDays.Contains(DayOfWeek.Thursday);
        FriCheckBox.IsChecked = _alarm.RepeatDays.Contains(DayOfWeek.Friday);
        SatCheckBox.IsChecked = _alarm.RepeatDays.Contains(DayOfWeek.Saturday);
        SunCheckBox.IsChecked = _alarm.RepeatDays.Contains(DayOfWeek.Sunday);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(HourTextBox.Text, out int hour) || hour is < 0 or > 23)
        {
            ErrorText.Text = "Hour must be 0-23.";
            return;
        }

        if (!int.TryParse(MinuteTextBox.Text, out int minute) || minute is < 0 or > 59)
        {
            ErrorText.Text = "Minute must be 0-59.";
            return;
        }

        if (!int.TryParse(SnoozeTextBox.Text, out int snoozeMinutes) || snoozeMinutes < 1)
        {
            ErrorText.Text = "Snooze must be at least 1 minute.";
            return;
        }

        _alarm.Time = new TimeSpan(hour, minute, 0);
        _alarm.Label = string.IsNullOrWhiteSpace(LabelTextBox.Text) ? "Alarm" : LabelTextBox.Text.Trim();
        _alarm.SnoozeMinutes = snoozeMinutes;
        _alarm.RepeatDays = GetRepeatDays();
        _alarm.IsTemporarySnooze = false;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private List<DayOfWeek> GetRepeatDays()
    {
        List<DayOfWeek> days = [];
        AddDayIfChecked(days, MonCheckBox, DayOfWeek.Monday);
        AddDayIfChecked(days, TueCheckBox, DayOfWeek.Tuesday);
        AddDayIfChecked(days, WedCheckBox, DayOfWeek.Wednesday);
        AddDayIfChecked(days, ThuCheckBox, DayOfWeek.Thursday);
        AddDayIfChecked(days, FriCheckBox, DayOfWeek.Friday);
        AddDayIfChecked(days, SatCheckBox, DayOfWeek.Saturday);
        AddDayIfChecked(days, SunCheckBox, DayOfWeek.Sunday);
        return days;
    }

    private static void AddDayIfChecked(List<DayOfWeek> days, CheckBox checkBox, DayOfWeek day)
    {
        if (checkBox.IsChecked == true)
        {
            days.Add(day);
        }
    }

    private static AlarmInfo CloneAlarm(AlarmInfo alarm)
    {
        return new AlarmInfo
        {
            Id = alarm.Id,
            Time = alarm.Time,
            Label = alarm.Label,
            IsEnabled = alarm.IsEnabled,
            RepeatDays = [.. alarm.RepeatDays],
            SnoozeMinutes = alarm.SnoozeMinutes,
            LastTriggeredDate = alarm.LastTriggeredDate,
            CreatedAt = alarm.CreatedAt,
            IsTemporarySnooze = alarm.IsTemporarySnooze
        };
    }
}
