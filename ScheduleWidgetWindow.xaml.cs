using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TimeWidget.Models;
using TimeWidget.Services;

namespace TimeWidget;

public partial class ScheduleWidgetWindow : Window
{
    private const int MaxSections = 12;
    private readonly TimeSpan[] _sectionStarts =
    [
        new(8, 0, 0), new(8, 55, 0), new(10, 0, 0), new(10, 55, 0),
        new(14, 0, 0), new(14, 55, 0), new(16, 0, 0), new(16, 55, 0),
        new(19, 0, 0), new(19, 55, 0), new(20, 50, 0), new(21, 45, 0)
    ];

    private readonly List<CourseScheduleInfo> _courses = [];
    private WidgetSettings _settings = new();
    private DateTime _termStartDate;
    private int _displayWeek;
    private bool _isLoaded;

    public ScheduleWidgetWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        Topmost = _settings.ScheduleTopmost;
        TopmostMenuItem.IsChecked = _settings.ScheduleTopmost;
        LockMenuItem.IsChecked = _settings.ScheduleIsLocked;
        ResizeMenuItem.IsChecked = _settings.ScheduleIsResizable;
        ThemeMenuItem.IsChecked = _settings.ScheduleIsLightMode;
        ApplyResizeMode();
        ApplyTheme();

        if (_settings.ScheduleWidth.HasValue && _settings.ScheduleHeight.HasValue)
        {
            Width = _settings.ScheduleWidth.Value;
            Height = Math.Min(_settings.ScheduleHeight.Value, 500);
        }

        RestorePosition();
        _termStartDate = GetTermStartDate();
        _displayWeek = GetCurrentWeek();
        _courses.Clear();
        _courses.AddRange(CourseScheduleStore.Load());
        RefreshSchedule();
        _isLoaded = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.ScheduleIsLocked)
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
        _settings.ScheduleTopmost = TopmostMenuItem.IsChecked;
        Topmost = _settings.ScheduleTopmost;
        SaveSettings();
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.ScheduleIsLocked = LockMenuItem.IsChecked;
        SaveSettings();
    }

    private void ResizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.ScheduleIsResizable = ResizeMenuItem.IsChecked;
        ApplyResizeMode();
        SaveSettings();
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.ScheduleIsLightMode = ThemeMenuItem.IsChecked;
        ApplyTheme();
        SaveSettings();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded || !_settings.ScheduleIsResizable)
        {
            return;
        }

        WindowSnapService.SnapResize(this);
        SaveSettings();
    }

    private void PreviousWeekButton_Click(object sender, RoutedEventArgs e)
    {
        _displayWeek = Math.Max(1, _displayWeek - 1);
        RefreshSchedule();
    }

    private void NextWeekButton_Click(object sender, RoutedEventArgs e)
    {
        _displayWeek = Math.Min(30, _displayWeek + 1);
        RefreshSchedule();
    }

    private void CurrentWeekButton_Click(object sender, RoutedEventArgs e) => GoCurrentWeek();
    private void CurrentWeekMenuItem_Click(object sender, RoutedEventArgs e) => GoCurrentWeek();
    private void AddCourseButton_Click(object sender, RoutedEventArgs e) => AddCourse();
    private void AddCourseMenuItem_Click(object sender, RoutedEventArgs e) => AddCourse();

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void GoCurrentWeek()
    {
        _displayWeek = GetCurrentWeek();
        RefreshSchedule();
    }

    private void AddCourse()
    {
        CourseEditDialog dialog = new()
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _courses.Add(dialog.Course);
            SaveCoursesAndRefresh();
        }
    }

    private void EditCourse(CourseScheduleInfo course)
    {
        CourseEditDialog dialog = new(course)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        int index = _courses.FindIndex(item => item.Id == course.Id);
        if (index >= 0)
        {
            _courses[index] = dialog.Course;
            SaveCoursesAndRefresh();
        }
    }

    private void DeleteCourse(CourseScheduleInfo course)
    {
        MessageBoxResult result = MessageBox.Show(this, $"Delete {course.CourseName}?", "Schedule", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _courses.RemoveAll(item => item.Id == course.Id);
        SaveCoursesAndRefresh();
    }

    private void SaveCoursesAndRefresh()
    {
        CourseScheduleStore.Save(_courses);
        RefreshSchedule();
    }

    private void RefreshSchedule()
    {
        DateTime today = DateTime.Today;
        WeekTitleText.Text = $"第 {_displayWeek} 周";
        DateText.Text = today.ToString("yyyy/MM/dd dddd", CultureInfo.InvariantCulture);
        BuildScheduleGrid();
        RefreshBottomInfo();
    }

    private void BuildScheduleGrid()
    {
        ScheduleGrid.Children.Clear();
        ScheduleGrid.RowDefinitions.Clear();
        ScheduleGrid.ColumnDefinitions.Clear();
        ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

        for (int day = 1; day <= 7; day++)
        {
            ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 40 });
        }

        ScheduleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        for (int section = 1; section <= MaxSections; section++)
        {
            ScheduleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        }

        int todayDay = ToCourseDay(DateTime.Today.DayOfWeek);
        AddCornerCell();
        for (int day = 1; day <= 7; day++)
        {
            AddDayHeader(day, day == todayDay);
        }

        for (int section = 1; section <= MaxSections; section++)
        {
            AddSectionHeader(section);
        }

        foreach (CourseScheduleInfo course in _courses.Where(IsCourseVisibleInWeek))
        {
            AddCourseCard(course);
        }
    }

    private void AddCornerCell()
    {
        TextBlock text = new()
        {
            Text = "#",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("MiSans"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = ResourceBrush("MutedText")
        };
        ScheduleGrid.Children.Add(text);
    }

    private void AddDayHeader(int day, bool isToday)
    {
        Border border = new()
        {
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Background = isToday ? ResourceBrush("HoverBackground") : Brushes.Transparent
        };
        TextBlock text = new()
        {
            Text = DayName(day),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("MiSans"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = isToday ? ResourceBrush("AccentText") : ResourceBrush("SecondaryText")
        };
        border.Child = text;
        Grid.SetColumn(border, day);
        ScheduleGrid.Children.Add(border);
    }

    private void AddSectionHeader(int section)
    {
        TextBlock text = new()
        {
            Text = section.ToString(CultureInfo.InvariantCulture),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("MiSans"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = ResourceBrush("MutedText")
        };
        Grid.SetRow(text, section);
        ScheduleGrid.Children.Add(text);
    }

    private void AddCourseCard(CourseScheduleInfo course)
    {
        bool isCurrent = IsCurrentCourse(course);
        Border card = new()
        {
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Background = ColorBrush(course.Color, isCurrent ? 0.95 : 0.78),
            BorderBrush = isCurrent ? ResourceBrush("AccentText") : ColorBrush("#FFFFFF", 0.18),
            BorderThickness = isCurrent ? new Thickness(2) : new Thickness(1),
            Padding = new Thickness(5),
            Tag = course,
            Cursor = Cursors.Hand,
            ToolTip = $"{course.CourseName}\n{course.Location}\n{course.Teacher}\nWeek {course.StartWeek}-{course.EndWeek} {FormatWeekType(course.WeekType)}"
        };

        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            Text = course.CourseName,
            FontFamily = new FontFamily("MiSans"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(course.Location) ? "No room" : course.Location,
            Margin = new Thickness(0, 2, 0, 0),
            FontFamily = new FontFamily("MiSans"),
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = ColorBrush("#FFFFFF", 0.82),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{course.StartSection}-{course.EndSection} · {course.Teacher}",
            Margin = new Thickness(0, 2, 0, 0),
            FontFamily = new FontFamily("MiSans"),
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = ColorBrush("#FFFFFF", 0.72),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        card.Child = panel;
        card.MouseLeftButtonDown += CourseCard_MouseLeftButtonDown;
        ContextMenu menu = new();
        MenuItem editItem = new() { Header = "Edit" };
        editItem.Click += (_, _) => EditCourse(course);
        MenuItem deleteItem = new() { Header = "Delete" };
        deleteItem.Click += (_, _) => DeleteCourse(course);
        menu.Items.Add(editItem);
        menu.Items.Add(deleteItem);
        card.ContextMenu = menu;

        Grid.SetColumn(card, course.DayOfWeek);
        Grid.SetRow(card, Math.Clamp(course.StartSection, 1, MaxSections));
        Grid.SetRowSpan(card, Math.Clamp(course.EndSection - course.StartSection + 1, 1, MaxSections));
        ScheduleGrid.Children.Add(card);
    }

    private void CourseCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && sender is Border { Tag: CourseScheduleInfo course })
        {
            EditCourse(course);
            e.Handled = true;
        }
    }

    private void RefreshBottomInfo()
    {
        int todayDay = ToCourseDay(DateTime.Today.DayOfWeek);
        List<CourseScheduleInfo> todayCourses = _courses
            .Where(item => item.DayOfWeek == todayDay && IsCourseVisibleInWeek(item))
            .OrderBy(item => item.StartSection)
            .ToList();

        TodayCoursesText.Text = todayCourses.Count == 0
            ? "No courses today"
            : string.Join("  ·  ", todayCourses.Select(item => $"{item.StartSection}-{item.EndSection} {item.CourseName}"));

        CourseScheduleInfo? nextCourse = todayCourses
            .Where(item => GetSectionStart(item.StartSection) > DateTime.Now.TimeOfDay)
            .OrderBy(item => item.StartSection)
            .FirstOrDefault();

        CourseScheduleInfo? currentCourse = todayCourses.FirstOrDefault(IsCurrentCourse);
        if (currentCourse is not null)
        {
            NextCourseText.Text = $"Now: {currentCourse.CourseName} · {currentCourse.Location}";
        }
        else if (nextCourse is not null)
        {
            NextCourseText.Text = $"Next: {GetSectionStart(nextCourse.StartSection):hh\\:mm} {nextCourse.CourseName} · {nextCourse.Location}";
        }
        else
        {
            NextCourseText.Text = "Next: no more courses today";
        }
    }

    private bool IsCourseVisibleInWeek(CourseScheduleInfo course)
    {
        if (_displayWeek < course.StartWeek || _displayWeek > course.EndWeek)
        {
            return false;
        }

        return course.WeekType switch
        {
            CourseWeekType.Odd => _displayWeek % 2 == 1,
            CourseWeekType.Even => _displayWeek % 2 == 0,
            _ => true
        };
    }

    private bool IsCurrentCourse(CourseScheduleInfo course)
    {
        if (course.DayOfWeek != ToCourseDay(DateTime.Today.DayOfWeek) || !IsCourseVisibleInWeek(course))
        {
            return false;
        }

        TimeSpan now = DateTime.Now.TimeOfDay;
        TimeSpan start = GetSectionStart(course.StartSection);
        TimeSpan end = GetSectionStart(course.EndSection).Add(TimeSpan.FromMinutes(45));
        return now >= start && now <= end;
    }

    private DateTime GetTermStartDate()
    {
        if (_settings.ScheduleTermStartDate.HasValue)
        {
            return StartOfWeek(_settings.ScheduleTermStartDate.Value.Date);
        }

        return StartOfWeek(DateTime.Today);
    }

    private int GetCurrentWeek()
    {
        int week = (int)Math.Floor((DateTime.Today.Date - _termStartDate).TotalDays / 7) + 1;
        return Math.Clamp(week, 1, 30);
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        int offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset).Date;
    }

    private void RestorePosition()
    {
        if (_settings.ScheduleLeft.HasValue && _settings.ScheduleTop.HasValue)
        {
            Left = _settings.ScheduleLeft.Value;
            Top = _settings.ScheduleTop.Value;
            WindowSnapService.KeepWindowOnScreen(this);
        }
    }

    private void SaveSettings()
    {
        SettingsStore.Update(settings =>
        {
            settings.ScheduleLeft = Left;
            settings.ScheduleTop = Top;
            settings.ScheduleWidth = Width;
            settings.ScheduleHeight = Height;
            settings.ScheduleTopmost = Topmost;
            settings.ScheduleIsLocked = LockMenuItem.IsChecked;
            settings.ScheduleIsResizable = ResizeMenuItem.IsChecked;
            settings.ScheduleIsLightMode = ThemeMenuItem.IsChecked;
            settings.ScheduleTermStartDate = _termStartDate;
        });
    }

    private void ApplyResizeMode()
    {
        MinWidth = 360;
        MinHeight = 420;
        ResizeMode = _settings.ScheduleIsResizable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
    }

    private void ApplyTheme()
    {
        bool light = _settings.ScheduleIsLightMode;
        Resources["MainBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#FFF7F8FC" : "#1F1E33"));
        Resources["SecondaryBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#FFECEFF6" : "#273142"));
        Resources["HoverBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#FFDDE6F2" : "#303B4F"));
        Resources["PrimaryText"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#172033" : "#EAF3FF"));
        Resources["SecondaryText"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#526175" : "#AAB8C8"));
        Resources["MutedText"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#7C8798" : "#7F8EA3"));
        Resources["AccentText"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#2C7DD2" : "#A7D8FF"));
        Resources["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#D3D9E5" : "#3A4658"));
        if (_isLoaded)
        {
            RefreshSchedule();
        }
    }

    private Brush ResourceBrush(string key)
    {
        return Resources[key] as Brush ?? Brushes.White;
    }

    private static Brush ColorBrush(string color, double opacity = 1)
    {
        SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(color));
        brush.Opacity = opacity;
        return brush;
    }

    private TimeSpan GetSectionStart(int section)
    {
        int index = Math.Clamp(section, 1, _sectionStarts.Length) - 1;
        return _sectionStarts[index];
    }

    private static int ToCourseDay(DayOfWeek day)
    {
        return day == DayOfWeek.Sunday ? 7 : (int)day;
    }

    private static string DayName(int day)
    {
        return day switch
        {
            1 => "Mon",
            2 => "Tue",
            3 => "Wed",
            4 => "Thu",
            5 => "Fri",
            6 => "Sat",
            _ => "Sun"
        };
    }

    private static string FormatWeekType(CourseWeekType weekType)
    {
        return weekType switch
        {
            CourseWeekType.Odd => "Odd",
            CourseWeekType.Even => "Even",
            _ => "All"
        };
    }
}
