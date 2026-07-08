using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TimeWidget.Models;

namespace TimeWidget;

public partial class CalendarEventEditDialog : Window
{
    private enum DialMode
    {
        Hour,
        Minute
    }

    private readonly DateTime _date;
    private readonly CalendarEventInfo? _sourceEvent;
    private DialMode _dialMode = DialMode.Hour;
    private int _selectedHour = DateTime.Now.Hour;
    private int _selectedMinute = RoundToFive(DateTime.Now.Minute);
    private bool _isUpdatingText;

    public CalendarEventInfo EventInfo { get; private set; }

    public CalendarEventEditDialog(DateTime date, CalendarEventInfo? eventInfo = null)
    {
        InitializeComponent();
        _date = date.Date;
        _sourceEvent = eventInfo;
        EventInfo = eventInfo is null ? new CalendarEventInfo { Date = _date } : CloneEvent(eventInfo);

        DateText.Text = _date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        TitleTextBox.Text = EventInfo.Title;
        DescriptionTextBox.Text = EventInfo.Description;

        if (TryParseTime(EventInfo.TimeText, out int hour, out int minute))
        {
            _selectedHour = hour;
            _selectedMinute = RoundToFive(minute);
        }

        SetTimeText(_selectedHour, _selectedMinute);
        RenderDial();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string title = TitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            ErrorText.Text = "Title is required.";
            return;
        }

        string hourText = HourTextBox.Text.Trim();
        string minuteText = MinuteTextBox.Text.Trim();
        string timeText = string.Empty;

        if (!string.IsNullOrWhiteSpace(hourText) || !string.IsNullOrWhiteSpace(minuteText))
        {
            if (!int.TryParse(hourText, CultureInfo.InvariantCulture, out int hour) || hour is < 0 or > 23)
            {
                ErrorText.Text = "Hour must be 00-23.";
                return;
            }

            if (!int.TryParse(minuteText, CultureInfo.InvariantCulture, out int minute) || minute is < 0 or > 55 || minute % 5 != 0)
            {
                ErrorText.Text = "Minute must use 5-minute steps.";
                return;
            }

            timeText = $"{hour:00}:{minute:00}";
        }

        EventInfo.Title = title;
        EventInfo.Date = _date;
        EventInfo.TimeText = timeText;
        EventInfo.Description = DescriptionTextBox.Text.Trim();
        EventInfo.UpdatedAt = DateTime.Now;
        EventInfo.CreatedAt = _sourceEvent?.CreatedAt ?? DateTime.Now;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void HourTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _dialMode = DialMode.Hour;
        RenderDial();
    }

    private void MinuteTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _dialMode = DialMode.Minute;
        RenderDial();
    }

    private void TimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingText)
        {
            return;
        }

        if (int.TryParse(HourTextBox.Text.Trim(), CultureInfo.InvariantCulture, out int hour) && hour is >= 0 and <= 23)
        {
            _selectedHour = hour;
        }

        if (int.TryParse(MinuteTextBox.Text.Trim(), CultureInfo.InvariantCulture, out int minute) && minute is >= 0 and <= 59)
        {
            _selectedMinute = RoundToFive(minute);
        }

        RenderDial();
    }

    private void NumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void RenderDial()
    {
        DialCanvas.Children.Clear();
        if (_dialMode == DialMode.Hour)
        {
            SetInputFocusVisual(HourTextBox, MinuteTextBox);
            RenderHourDial();
            UpdateDialHand((_selectedHour % 12) * 30, _selectedHour >= 12 ? 45 : 68);
        }
        else
        {
            SetInputFocusVisual(MinuteTextBox, HourTextBox);
            RenderMinuteDial();
            UpdateDialHand(_selectedMinute / 5 * 30, 68);
        }
    }

    private void RenderHourDial()
    {
        for (int hour = 0; hour < 24; hour++)
        {
            int value = hour;
            double radius = value < 12 ? 68 : 45;
            AddDialButton(value.ToString("00", CultureInfo.InvariantCulture), value == _selectedHour, radius, (value % 12) * 30, () =>
            {
                _selectedHour = value;
                SetTimeText(_selectedHour, _selectedMinute);
                RenderDial();
            });
        }
    }

    private void RenderMinuteDial()
    {
        for (int minute = 0; minute < 60; minute += 5)
        {
            int value = minute;
            AddDialButton(value.ToString("00", CultureInfo.InvariantCulture), value == _selectedMinute, 68, value / 5 * 30, () =>
            {
                _selectedMinute = value;
                SetTimeText(_selectedHour, _selectedMinute);
                RenderDial();
            });
        }
    }

    private void AddDialButton(string text, bool isSelected, double radius, double angleDegrees, Action click)
    {
        const double center = 92;
        const double size = 28;
        double radians = (angleDegrees - 90) * Math.PI / 180;

        Border button = new()
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = Brush(isSelected ? "#A7D8FF" : "#1AFFFFFF"),
            BorderBrush = Brush("#334658"),
            BorderThickness = new Thickness(isSelected ? 0 : 1),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush(isSelected ? "#1F1E33" : "#EAF3FF"),
                FontFamily = new FontFamily("MiSans"),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            }
        };
        button.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            click();
        };

        Canvas.SetLeft(button, center + Math.Cos(radians) * radius - size / 2);
        Canvas.SetTop(button, center + Math.Sin(radians) * radius - size / 2);
        DialCanvas.Children.Add(button);
    }

    private void SetTimeText(int hour, int minute)
    {
        _isUpdatingText = true;
        HourTextBox.Text = hour.ToString("00", CultureInfo.InvariantCulture);
        MinuteTextBox.Text = minute.ToString("00", CultureInfo.InvariantCulture);
        _isUpdatingText = false;
    }

    private void UpdateDialHand(double angleDegrees, double radius)
    {
        const double center = 92;
        double radians = (angleDegrees - 90) * Math.PI / 180;
        DialHand.X1 = center;
        DialHand.Y1 = center;
        DialHand.X2 = center + Math.Cos(radians) * radius;
        DialHand.Y2 = center + Math.Sin(radians) * radius;
    }

    private static void SetInputFocusVisual(TextBox active, TextBox inactive)
    {
        active.Background = Brush("#36506C");
        active.BorderBrush = Brush("#A7D8FF");
        inactive.Background = Brush("#273142");
        inactive.BorderBrush = Brush("#3A4658");
    }

    private static bool TryParseTime(string timeText, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;
        if (!DateTime.TryParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            return false;
        }

        hour = parsed.Hour;
        minute = parsed.Minute;
        return true;
    }

    private static int RoundToFive(int minute)
    {
        int rounded = (int)Math.Round(minute / 5.0, MidpointRounding.AwayFromZero) * 5;
        return rounded >= 60 ? 55 : Math.Clamp(rounded, 0, 55);
    }

    private static Brush Brush(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private static CalendarEventInfo CloneEvent(CalendarEventInfo eventInfo)
    {
        return new CalendarEventInfo
        {
            Id = eventInfo.Id,
            Date = eventInfo.Date.Date,
            Title = eventInfo.Title,
            TimeText = eventInfo.TimeText,
            Description = eventInfo.Description,
            CreatedAt = eventInfo.CreatedAt,
            UpdatedAt = eventInfo.UpdatedAt
        };
    }
}
