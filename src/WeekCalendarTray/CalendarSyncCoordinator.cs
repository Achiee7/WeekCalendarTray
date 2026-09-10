using System.Net.Http;
using WeekCalendarTray.Core;

namespace WeekCalendarTray;

internal sealed class CalendarSyncCoordinator : IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly SyncSettingsStore _settingsStore = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public CalendarSyncCoordinator()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WeekCalendarTray/1.1");
    }

    public event EventHandler? EventsChanged;

    public event EventHandler? SettingsChanged;

    public SyncSettingsStore SettingsStore => _settingsStore;

    public void NotifySettingsChanged()
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsForDateAsync(DateOnly date)
    {
        var cache = await CalendarEventCache.LoadAsync();
        return cache.GetEventsFor(date);
    }

    public async Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!await _syncLock.WaitAsync(0, cancellationToken))
        {
            var busy = new SyncResult();
            busy.Messages.Add("Calendar sync is already running.");
            return busy;
        }
        try
        {
            var settings = await _settingsStore.LoadAsync();
            var enabledSubscriptions = settings.Subscriptions
                .Where(subscription => subscription.IsEnabled && !string.IsNullOrWhiteSpace(subscription.Url))
                .ToList();
            var cache = await CalendarEventCache.LoadAsync();
            var result = new SyncResult();
            var now = DateTimeOffset.Now;
            var start = now.Date.AddDays(-Math.Max(0, settings.SyncPastDays));
            var end = now.Date.AddDays(Math.Max(1, settings.SyncFutureDays) + 1);
            var client = new IcalCalendarClient(_httpClient);

            cache.RemoveSourcesExcept(enabledSubscriptions.Select(subscription => subscription.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));

            foreach (var subscription in enabledSubscriptions)
            {
                try
                {
                    var events = await client.FetchEventsAsync(subscription, start, end, cancellationToken);
                    cache.ReplaceSourceEvents(subscription.Id, events);
                    result.Events += events.Count;
                    result.Calendars++;
                    result.Messages.Add($"{subscription.Name}: {events.Count} events.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    AppDiagnostics.Log("Fetch calendar feed", ex);
                    result.HasErrors = true;
                    result.Messages.Add($"{subscription.Name}: sync failed; previous events kept.");
                }
            }

            if (enabledSubscriptions.Count == 0)
            {
                result.Messages.Add("No calendar links. Open Settings and add an iCal/ICS URL.");
            }

            cache.LastIcalSyncUtc = DateTimeOffset.UtcNow;
            await cache.SaveAsync();
            EventsChanged?.Invoke(this, EventArgs.Empty);
            return result;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
