namespace TimeWidget.Models;

public sealed class CourseScheduleInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CourseName { get; set; } = string.Empty;
    public string Teacher { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public int DayOfWeek { get; set; } = 1;
    public int StartSection { get; set; } = 1;
    public int EndSection { get; set; } = 1;
    public int StartWeek { get; set; } = 1;
    public int EndWeek { get; set; } = 18;
    public CourseWeekType WeekType { get; set; } = CourseWeekType.All;
    public string Color { get; set; } = "#5E8CFF";
    public string Note { get; set; } = string.Empty;
}
