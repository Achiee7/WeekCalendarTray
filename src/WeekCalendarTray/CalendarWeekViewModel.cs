namespace WeekCalendarTray;

public sealed class CalendarWeekViewModel(int isoWeekNumber, IReadOnlyList<CalendarDayViewModel> days)
{
    public int IsoWeekNumber { get; } = isoWeekNumber;

    public IReadOnlyList<CalendarDayViewModel> Days { get; } = days;
}
