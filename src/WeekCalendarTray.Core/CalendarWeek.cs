namespace WeekCalendarTray.Core;

public sealed record CalendarWeek(int IsoWeekNumber, IReadOnlyList<CalendarDay> Days);
