using WeekCalendarTray.Core;

namespace WeekCalendarTray;

internal sealed class CalendarEventCache
{
    public DateTimeOffset? LastIcalSyncUtc { get; set; }

    public List<CalendarEvent> Events { get; set; } = [];

    public static async Task<CalendarEventCache> LoadAsync()
    {
        var cache = await AtomicJsonFile.LoadAsync(AppPaths.EventCachePath, () => new CalendarEventCache(),
            ex => AppDiagnostics.Log("Restore calendar cache backup", ex));
        cache.Events ??= [];
        cache.Events.RemoveAll(item => item is null);
        return cache;
    }

    public async Task SaveAsync()
    {
        await AtomicJsonFile.SaveAsync(AppPaths.EventCachePath, this);
    }

    public void ReplaceSourceEvents(string sourceId, IEnumerable<CalendarEvent> events)
    {
        Events.RemoveAll(calendarEvent => string.Equals(calendarEvent.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));
        Events.AddRange(events);
        Events = Events
            .OrderBy(calendarEvent => calendarEvent.Start)
            .ThenBy(calendarEvent => calendarEvent.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void RemoveSourcesExcept(IReadOnlySet<string> sourceIds)
    {
        Events.RemoveAll(calendarEvent => !sourceIds.Contains(calendarEvent.SourceId));
    }

    public IReadOnlyList<CalendarEvent> GetEventsFor(DateOnly date)
    {
        return Events
            .Where(calendarEvent => calendarEvent.OccursOn(date))
            .OrderBy(calendarEvent => calendarEvent.IsAllDay ? 0 : 1)
            .ThenBy(calendarEvent => calendarEvent.Start)
            .ThenBy(calendarEvent => calendarEvent.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
