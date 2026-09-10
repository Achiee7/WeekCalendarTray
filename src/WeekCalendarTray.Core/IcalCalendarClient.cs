using System.Net.Http;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;

namespace WeekCalendarTray.Core;

public sealed class IcalCalendarClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<CalendarEvent>> FetchEventsAsync(
        IcalSubscription subscription,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var url = NormalizeUrl(subscription.Url);
        var ics = await httpClient.GetStringAsync(url, cancellationToken);
        return IcalFeedParser.Parse(ics, subscription, start, end);
    }

    private static Uri NormalizeUrl(string rawUrl)
    {
        var trimmed = CleanPastedUrl(rawUrl);
        if (trimmed.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed["webcal://".Length..];
        }

        if (trimmed.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("/calendar.html", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This looks like the Outlook HTML link. Copy the ICS link instead.");
        }

        return new Uri(trimmed, UriKind.Absolute);
    }

    private static string CleanPastedUrl(string value)
    {
        var trimmed = value.Trim();
        foreach (var prefix in new[] { "ICS:", "iCal:", "HTML:" })
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].Trim();
            }
        }

        return trimmed;
    }
}

public static class IcalFeedParser
{
    private const int MaxOccurrencesPerFeed = 5000;

    public static IReadOnlyList<CalendarEvent> Parse(
        string ics,
        IcalSubscription subscription,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        if (!ics.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The link did not return an iCal calendar. Copy the ICS link, not the HTML link.");
        }

        var calendar = Ical.Net.Calendar.Load(ics);
        if (calendar is null)
        {
            return [];
        }

        var periodStart = new CalDateTime(start.UtcDateTime, CalDateTime.UtcTzId);
        var events = new List<CalendarEvent>();

        foreach (var occurrence in calendar.GetOccurrences(periodStart).Take(MaxOccurrencesPerFeed))
        {
            var calendarEvent = occurrence.Source as Ical.Net.CalendarComponents.CalendarEvent;
            if (calendarEvent is null)
            {
                continue;
            }

            var startTime = occurrence.Period.StartTime;
            if (startTime is null)
            {
                continue;
            }

            var isAllDay = IsAllDay(calendarEvent);
            var occurrenceStart = isAllDay ? ToAllDayDateTimeOffset(startTime) : ToDateTimeOffset(startTime);
            var occurrenceEnd = isAllDay
                ? ResolveAllDayOccurrenceEnd(calendarEvent, occurrenceStart)
                : ResolveOccurrenceEnd(occurrence.Period.EndTime, calendarEvent, occurrenceStart);
            if (occurrenceEnd <= occurrenceStart)
            {
                occurrenceEnd = occurrenceStart.AddMinutes(1);
            }

            if (occurrenceEnd <= start || occurrenceStart >= end)
            {
                continue;
            }

            var uid = string.IsNullOrWhiteSpace(calendarEvent.Uid)
                ? Guid.NewGuid().ToString("N")
                : calendarEvent.Uid;
            var eventId = $"{subscription.Id}:{uid}:{occurrenceStart.UtcDateTime:O}";

            events.Add(new CalendarEvent(
                subscription.Id,
                subscription.Name,
                eventId,
                string.IsNullOrWhiteSpace(calendarEvent.Summary) ? "(No title)" : calendarEvent.Summary,
                occurrenceStart,
                occurrenceEnd,
                isAllDay,
                CleanText(calendarEvent.Location),
                CleanText(calendarEvent.Description),
                GetOrganizer(calendarEvent),
                calendarEvent.Url?.ToString()));
        }

        return events
            .OrderBy(calendarEvent => calendarEvent.Start)
            .ThenBy(calendarEvent => calendarEvent.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static DateTimeOffset ResolveOccurrenceEnd(
        CalDateTime? occurrenceEndTime,
        Ical.Net.CalendarComponents.CalendarEvent calendarEvent,
        DateTimeOffset occurrenceStart)
    {
        if (occurrenceEndTime is not null)
        {
            return ToDateTimeOffset(occurrenceEndTime);
        }

        var sourceDuration = GetSourceDuration(calendarEvent);
        if (sourceDuration is { } duration && duration > TimeSpan.Zero)
        {
            return occurrenceStart.Add(duration);
        }

        return occurrenceStart.AddMinutes(1);
    }

    private static DateTimeOffset ResolveAllDayOccurrenceEnd(
        Ical.Net.CalendarComponents.CalendarEvent calendarEvent,
        DateTimeOffset occurrenceStart)
    {
        var days = 1;
        if (calendarEvent.Start is not null && calendarEvent.End is not null)
        {
            days = Math.Max(1, (calendarEvent.End.Value.Date - calendarEvent.Start.Value.Date).Days);
        }

        return occurrenceStart.AddDays(days);
    }

    private static TimeSpan? GetSourceDuration(Ical.Net.CalendarComponents.CalendarEvent calendarEvent)
    {
        if (calendarEvent.Start is null || calendarEvent.End is null)
        {
            return null;
        }

        var sourceStart = ToDateTimeOffset(calendarEvent.Start);
        var sourceEnd = ToDateTimeOffset(calendarEvent.End);
        return sourceEnd - sourceStart;
    }

    private static DateTimeOffset ToDateTimeOffset(CalDateTime value)
    {
        var utc = value.AsUtc;
        return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToLocalTime();
    }

    private static DateTimeOffset ToAllDayDateTimeOffset(CalDateTime value)
    {
        var date = value.Value.Date;
        return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
    }

    private static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static string? GetOrganizer(Ical.Net.CalendarComponents.CalendarEvent calendarEvent)
    {
        var organizer = calendarEvent.Organizer;
        if (organizer is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(organizer.CommonName))
        {
            return organizer.CommonName.Trim();
        }

        var value = organizer.Value?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        const string mailToPrefix = "mailto:";
        return value.StartsWith(mailToPrefix, StringComparison.OrdinalIgnoreCase)
            ? value[mailToPrefix.Length..].Trim()
            : value.Trim();
    }

    private static bool IsAllDay(Ical.Net.CalendarComponents.CalendarEvent calendarEvent)
    {
        return calendarEvent.Start is not null
            && calendarEvent.End is not null
            && calendarEvent.Start.Value.TimeOfDay == TimeSpan.Zero
            && calendarEvent.End.Value.TimeOfDay == TimeSpan.Zero
            && calendarEvent.End.Value.Date > calendarEvent.Start.Value.Date;
    }
}
