namespace WeekCalendarTray.Core;

public sealed record CalendarEvent(
    string SourceId,
    string SourceName,
    string Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    string? Organizer,
    string? SourceUrl)
{
    public bool OccursOn(DateOnly date)
    {
        var startDate = DateOnly.FromDateTime(Start.LocalDateTime);
        var inclusiveEnd = End > Start ? End.LocalDateTime.AddTicks(-1) : Start.LocalDateTime;
        var endDate = DateOnly.FromDateTime(inclusiveEnd);

        return date >= startDate && date <= endDate;
    }
}
