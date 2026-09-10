namespace WeekCalendarTray.Core;

public sealed record CalendarDay(
    DateOnly Date,
    int DayNumber,
    int IsoWeekNumber,
    bool IsCurrentMonth,
    bool IsToday,
    bool IsSelected);
