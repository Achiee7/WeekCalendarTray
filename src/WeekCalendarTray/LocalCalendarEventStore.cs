using WeekCalendarTray.Core;

namespace WeekCalendarTray;

internal sealed class LocalCalendarEvent
{
    public const string LocalSourceId = "local-events";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset Start { get; set; }

    public DateTimeOffset End { get; set; }

    public bool IsAllDay { get; set; }

    public string? Location { get; set; }

    public string? Description { get; set; }

    public CalendarEvent ToCalendarEvent()
    {
        return new CalendarEvent(
            LocalSourceId,
            "Local",
            Id,
            string.IsNullOrWhiteSpace(Title) ? "(No title)" : Title,
            Start,
            End,
            IsAllDay,
            Clean(Location),
            Clean(Description),
            null,
            null);
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

internal sealed class LocalCalendarEventStore
{
    public List<LocalCalendarEvent> Events { get; set; } = [];

    public static async Task<LocalCalendarEventStore> LoadAsync()
    {
        var store = await AtomicJsonFile.LoadAsync(AppPaths.LocalEventsPath, () => new LocalCalendarEventStore(),
            ex => AppDiagnostics.Log("Restore local events backup", ex));
        store.Events ??= [];
        store.Events.RemoveAll(item => item is null);
        return store;
    }

    public async Task SaveAsync()
    {
        await AtomicJsonFile.SaveAsync(AppPaths.LocalEventsPath, this);
    }

    public void Add(LocalCalendarEvent calendarEvent)
    {
        Events.Add(calendarEvent);
        Events = Events
            .OrderBy(item => item.Start)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public bool Remove(string id)
    {
        return Events.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    public IReadOnlyList<CalendarEvent> GetEventsFor(DateOnly date)
    {
        return Events
            .Select(localEvent => localEvent.ToCalendarEvent())
            .Where(calendarEvent => calendarEvent.OccursOn(date))
            .OrderBy(calendarEvent => calendarEvent.IsAllDay ? 0 : 1)
            .ThenBy(calendarEvent => calendarEvent.Start)
            .ThenBy(calendarEvent => calendarEvent.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
