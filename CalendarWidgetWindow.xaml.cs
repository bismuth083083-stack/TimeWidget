using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class CalendarWidgetWindow : Window
{
    private const int MinYear = 2000;
    private const int MaxYear = 2099;

    private readonly List<CalendarEventInfo> _events = [];
    private WidgetSettings _settings = new();
    private DateTime _displayMonth;
    private DateTime _selectedDate;

    public CalendarWidgetWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        Topmost = _settings.CalendarTopmost;
        TopmostMenuItem.IsChecked = _settings.CalendarTopmost;
        LockMenuItem.IsChecked = _settings.CalendarIsLocked;
        RestorePosition();

        _events.Clear();
        _events.AddRange(CalendarEventStore.Load());

        DateTime today = DateTime.Today;
        _selectedDate = IsInRange(today) ? today : new DateTime(2026, 1, 1);
        _displayMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        RefreshCalendar();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.CalendarIsLocked)
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
        _settings.CalendarTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.CalendarTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.CalendarIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void TodayMenuItem_Click(object sender, RoutedEventArgs e) => GoToday();
    private void TodayButton_Click(object sender, RoutedEventArgs e) => GoToday();

    private void AddEventMenuItem_Click(object sender, RoutedEventArgs e)
    {
        AddEvent(_selectedDate);
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PreviousMonthButton_Click(object sender, RoutedEventArgs e)
    {
        ChangeMonth(-1);
    }

    private void NextMonthButton_Click(object sender, RoutedEventArgs e)
    {
        ChangeMonth(1);
    }

    private void EventsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EventsListBox.SelectedItem is CalendarEventInfo eventInfo)
        {
            EditEvent(eventInfo);
        }
    }

    private void EditEventButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: CalendarEventInfo eventInfo })
        {
            EditEvent(eventInfo);
        }
    }

    private void DeleteEventButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: CalendarEventInfo eventInfo })
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(this, "Delete this event?", "Calendar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _events.Remove(eventInfo);
        SaveEventsAndRefresh();
    }

    private void ChangeMonth(int delta)
    {
        DateTime target = _displayMonth.AddMonths(delta);
        if (target.Year < MinYear || target.Year > MaxYear)
        {
            return;
        }

        int selectedDay = Math.Min(_selectedDate.Day, DateTime.DaysInMonth(target.Year, target.Month));
        _displayMonth = target;
        _selectedDate = new DateTime(target.Year, target.Month, selectedDay);
        RefreshCalendar();
    }

    private void GoToday()
    {
        DateTime today = DateTime.Today;
        if (!IsInRange(today))
        {
            today = new DateTime(2026, 1, 1);
        }

        _selectedDate = today;
        _displayMonth = new DateTime(today.Year, today.Month, 1);
        RefreshCalendar();
    }

    private void RefreshCalendar()
    {
        string month = _displayMonth.ToString("MMMM", CultureInfo.InvariantCulture);
        MonthTitleText.Text = $"{month} {_displayMonth.Year.ToString(CultureInfo.InvariantCulture)}";
        MonthTitleText.HighlightStart = 0;
        MonthTitleText.HighlightLength = Math.Min(3, month.Length);
        CalendarDaysGrid.Children.Clear();

        int firstOffset = ((int)_displayMonth.DayOfWeek + 6) % 7;
        DateTime firstCellDate = _displayMonth.AddDays(-firstOffset);

        for (int i = 0; i < 42; i++)
        {
            CalendarDaysGrid.Children.Add(CreateDayCell(firstCellDate.AddDays(i)));
        }

        RefreshEventsList();
    }

    private Border CreateDayCell(DateTime date)
    {
        bool isCurrentMonth = date.Month == _displayMonth.Month && date.Year == _displayMonth.Year;
        bool isSelected = date.Date == _selectedDate.Date;
        bool isToday = date.Date == DateTime.Today;
        bool hasEvents = isCurrentMonth && _events.Any(item => item.Date.Date == date.Date);
        bool isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        Border border = new()
        {
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            BorderBrush = isToday ? Brush("#A7D8FF") : Brushes.Transparent,
            BorderThickness = isToday ? new Thickness(1) : new Thickness(0),
            Background = isSelected ? Brush("#36506C") : Brushes.Transparent,
            Tag = date
        };

        StackPanel panel = new()
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(new TextBlock
        {
            Text = date.Day.ToString(CultureInfo.InvariantCulture),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = new FontFamily("MiSans"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = isCurrentMonth
                ? isWeekend ? Brush("#AAB8C8") : Brush("#EAF3FF")
                : Brush("#566274")
        });

        panel.Children.Add(new Border
        {
            Width = 4,
            Height = 4,
            Margin = new Thickness(0, 4, 0, 0),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = hasEvents ? Brush("#A7D8FF") : Brushes.Transparent
        });

        border.Child = panel;
        border.MouseLeftButtonDown += DayCell_MouseLeftButtonDown;
        border.MouseEnter += (_, _) =>
        {
            if (!isSelected)
            {
                border.Background = Brush("#303B4F");
            }
        };
        border.MouseLeave += (_, _) =>
        {
            border.Background = isSelected ? Brush("#36506C") : Brushes.Transparent;
        };

        return border;
    }

    private void DayCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: DateTime date })
        {
            return;
        }

        _selectedDate = date.Date;
        if (date.Year != _displayMonth.Year || date.Month != _displayMonth.Month)
        {
            _displayMonth = new DateTime(date.Year, date.Month, 1);
        }

        RefreshCalendar();
        if (e.ClickCount >= 2)
        {
            AddEvent(date);
        }
    }

    private void RefreshEventsList()
    {
        SelectedDateText.Text = _selectedDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        List<CalendarEventInfo> dayEvents = _events
            .Where(item => item.Date.Date == _selectedDate.Date)
            .OrderBy(item => item.TimeText)
            .ToList();

        EventsListBox.ItemsSource = dayEvents;
        NoEventsText.Visibility = dayEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EventsListBox.Visibility = dayEvents.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddEvent(DateTime date)
    {
        CalendarEventEditDialog dialog = new(date.Date)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _events.Add(dialog.EventInfo);
            SaveEventsAndRefresh();
        }
    }

    private void EditEvent(CalendarEventInfo eventInfo)
    {
        CalendarEventEditDialog dialog = new(eventInfo.Date.Date, eventInfo)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        int index = _events.FindIndex(item => item.Id == eventInfo.Id);
        if (index >= 0)
        {
            _events[index] = dialog.EventInfo;
            SaveEventsAndRefresh();
        }
    }

    private void SaveEventsAndRefresh()
    {
        CalendarEventStore.Save(_events);
        RefreshCalendar();
    }

    private void RestorePosition()
    {
        if (_settings.CalendarLeft.HasValue && _settings.CalendarTop.HasValue)
        {
            Left = _settings.CalendarLeft.Value;
            Top = _settings.CalendarTop.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.CalendarLeft = Left;
            settings.CalendarTop = Top;
            settings.CalendarTopmost = Topmost;
            settings.CalendarIsLocked = LockMenuItem.IsChecked;
        });
    }

    private static bool IsInRange(DateTime date)
    {
        return date.Year is >= MinYear and <= MaxYear;
    }

    private static Brush Brush(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }
}
