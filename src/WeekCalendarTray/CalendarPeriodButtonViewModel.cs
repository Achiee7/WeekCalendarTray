namespace WeekCalendarTray;

public sealed class CalendarPeriodButtonViewModel(
    string label,
    int value,
    bool isCurrentPeriod,
    bool isSelectedPeriod)
{
    public string Label => label;

    public int Value => value;

    public bool IsCurrentPeriod => isCurrentPeriod;

    public bool IsSelectedPeriod => isSelectedPeriod;
}
