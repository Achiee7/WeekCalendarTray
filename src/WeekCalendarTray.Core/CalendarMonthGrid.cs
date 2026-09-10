namespace WeekCalendarTray.Core;

public sealed record CalendarMonthGrid(
    DateOnly VisibleMonth,
    DateOnly SelectedDate,
    DateOnly Today,
    IReadOnlyList<CalendarWeek> Weeks);
