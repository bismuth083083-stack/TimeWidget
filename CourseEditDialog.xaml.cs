using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TimeWidget.Models;

namespace TimeWidget;

public partial class CourseEditDialog : Window
{
    private const string CourseNamePlaceholder = "课程名称，例如 高等数学";
    private const string TeacherPlaceholder = "教师，例如 王老师";
    private const string LocationPlaceholder = "教室，例如 A101";
    private const string StartSectionPlaceholder = "开始节次 1-12";
    private const string EndSectionPlaceholder = "结束节次 1-12";
    private const string StartWeekPlaceholder = "开始周 1-30";
    private const string EndWeekPlaceholder = "结束周 1-30";
    private const string NotePlaceholder = "备注，可选";

    private static readonly Brush PlaceholderBrush = new SolidColorBrush(Color.FromRgb(127, 142, 163));
    private static readonly Brush InputBrush = new SolidColorBrush(Color.FromRgb(234, 243, 255));

    private readonly CourseScheduleInfo? _sourceCourse;

    public CourseScheduleInfo Course { get; private set; } = new();

    public CourseEditDialog(CourseScheduleInfo? sourceCourse = null)
    {
        InitializeComponent();
        _sourceCourse = sourceCourse;
        InitializeOptions();
        LoadCourse(sourceCourse);
    }

    private void InitializeOptions()
    {
        DayComboBox.ItemsSource = new[]
        {
            new ComboOption<int>("Mon", 1),
            new ComboOption<int>("Tue", 2),
            new ComboOption<int>("Wed", 3),
            new ComboOption<int>("Thu", 4),
            new ComboOption<int>("Fri", 5),
            new ComboOption<int>("Sat", 6),
            new ComboOption<int>("Sun", 7)
        };
        DayComboBox.DisplayMemberPath = nameof(ComboOption<int>.Label);
        DayComboBox.SelectedValuePath = nameof(ComboOption<int>.Value);

        WeekTypeComboBox.ItemsSource = new[]
        {
            new ComboOption<CourseWeekType>("All weeks", CourseWeekType.All),
            new ComboOption<CourseWeekType>("Odd weeks", CourseWeekType.Odd),
            new ComboOption<CourseWeekType>("Even weeks", CourseWeekType.Even)
        };
        WeekTypeComboBox.DisplayMemberPath = nameof(ComboOption<CourseWeekType>.Label);
        WeekTypeComboBox.SelectedValuePath = nameof(ComboOption<CourseWeekType>.Value);

        ColorComboBox.ItemsSource = new[]
        {
            new ComboOption<string>("Blue", "#5E8CFF"),
            new ComboOption<string>("Cyan", "#49C6E5"),
            new ComboOption<string>("Green", "#7EC8A9"),
            new ComboOption<string>("Orange", "#FFB86C"),
            new ComboOption<string>("Pink", "#F48FB1"),
            new ComboOption<string>("Purple", "#B69CFF")
        };
        ColorComboBox.DisplayMemberPath = nameof(ComboOption<string>.Label);
        ColorComboBox.SelectedValuePath = nameof(ComboOption<string>.Value);
    }

    private void LoadCourse(CourseScheduleInfo? course)
    {
        if (course is null)
        {
            SetPlaceholder(CourseNameTextBox, CourseNamePlaceholder);
            SetPlaceholder(TeacherTextBox, TeacherPlaceholder);
            SetPlaceholder(LocationTextBox, LocationPlaceholder);
            DayComboBox.SelectedValue = (int)Math.Clamp(((int)DateTime.Today.DayOfWeek + 6) % 7 + 1, 1, 7);
            WeekTypeComboBox.SelectedValue = CourseWeekType.All;
            SetPlaceholder(StartSectionTextBox, StartSectionPlaceholder);
            SetPlaceholder(EndSectionTextBox, EndSectionPlaceholder);
            SetPlaceholder(StartWeekTextBox, StartWeekPlaceholder);
            SetPlaceholder(EndWeekTextBox, EndWeekPlaceholder);
            ColorComboBox.SelectedValue = "#5E8CFF";
            SetPlaceholder(NoteTextBox, NotePlaceholder);
            return;
        }

        CourseNameTextBox.Text = course.CourseName;
        TeacherTextBox.Text = course.Teacher;
        LocationTextBox.Text = course.Location;
        DayComboBox.SelectedValue = course.DayOfWeek;
        WeekTypeComboBox.SelectedValue = course.WeekType;
        StartSectionTextBox.Text = course.StartSection.ToString();
        EndSectionTextBox.Text = course.EndSection.ToString();
        StartWeekTextBox.Text = course.StartWeek.ToString();
        EndWeekTextBox.Text = course.EndWeek.ToString();
        ColorComboBox.SelectedValue = course.Color;
        NoteTextBox.Text = course.Note;

        ApplyPlaceholderIfEmpty(TeacherTextBox, TeacherPlaceholder);
        ApplyPlaceholderIfEmpty(LocationTextBox, LocationPlaceholder);
        ApplyPlaceholderIfEmpty(NoteTextBox, NotePlaceholder);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        string name = ReadText(CourseNameTextBox);
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Course name is required.";
            return;
        }

        if (!TryReadNumber(ReadText(StartSectionTextBox), 1, 12, out int startSection)
            || !TryReadNumber(ReadText(EndSectionTextBox), 1, 12, out int endSection)
            || startSection > endSection)
        {
            ErrorText.Text = "Sections must be 1-12, and start must be before end.";
            return;
        }

        if (!TryReadNumber(ReadText(StartWeekTextBox), 1, 30, out int startWeek)
            || !TryReadNumber(ReadText(EndWeekTextBox), 1, 30, out int endWeek)
            || startWeek > endWeek)
        {
            ErrorText.Text = "Weeks must be 1-30, and start must be before end.";
            return;
        }

        Course = new CourseScheduleInfo
        {
            Id = _sourceCourse?.Id ?? Guid.NewGuid(),
            CourseName = name,
            Teacher = ReadText(TeacherTextBox),
            Location = ReadText(LocationTextBox),
            DayOfWeek = DayComboBox.SelectedValue is int day ? day : 1,
            StartSection = startSection,
            EndSection = endSection,
            StartWeek = startWeek,
            EndWeek = endWeek,
            WeekType = WeekTypeComboBox.SelectedValue is CourseWeekType weekType ? weekType : CourseWeekType.All,
            Color = ColorComboBox.SelectedValue as string ?? "#5E8CFF",
            Note = ReadText(NoteTextBox)
        };

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PlaceholderTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.Tag is string placeholder && textBox.Text == placeholder)
        {
            textBox.Text = string.Empty;
            textBox.Foreground = InputBrush;
        }
    }

    private void PlaceholderTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            string placeholder = GetPlaceholder(textBox);
            ApplyPlaceholderIfEmpty(textBox, placeholder);
        }
    }

    private static bool TryReadNumber(string value, int min, int max, out int number)
    {
        return int.TryParse(value, out number) && number >= min && number <= max;
    }

    private static void ApplyPlaceholderIfEmpty(TextBox textBox, string placeholder)
    {
        if (string.IsNullOrWhiteSpace(textBox.Text))
        {
            SetPlaceholder(textBox, placeholder);
        }
        else
        {
            textBox.Tag = placeholder;
            textBox.Foreground = InputBrush;
        }
    }

    private static void SetPlaceholder(TextBox textBox, string placeholder)
    {
        textBox.Tag = placeholder;
        textBox.Text = placeholder;
        textBox.Foreground = PlaceholderBrush;
    }

    private static string ReadText(TextBox textBox)
    {
        if (textBox.Tag is string placeholder && textBox.Text == placeholder)
        {
            return string.Empty;
        }

        return textBox.Text.Trim();
    }

    private string GetPlaceholder(TextBox textBox)
    {
        return textBox == CourseNameTextBox
            ? CourseNamePlaceholder
            : textBox == TeacherTextBox
                ? TeacherPlaceholder
                : textBox == LocationTextBox
                    ? LocationPlaceholder
                    : textBox == StartSectionTextBox
                        ? StartSectionPlaceholder
                        : textBox == EndSectionTextBox
                            ? EndSectionPlaceholder
                            : textBox == StartWeekTextBox
                                ? StartWeekPlaceholder
                                : textBox == EndWeekTextBox
                                    ? EndWeekPlaceholder
                                    : NotePlaceholder;
    }

    private sealed record ComboOption<T>(string Label, T Value);
}
