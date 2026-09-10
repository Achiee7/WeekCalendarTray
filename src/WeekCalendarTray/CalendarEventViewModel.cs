using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using WeekCalendarTray.Core;
using Media = System.Windows.Media;

namespace WeekCalendarTray;

public sealed class CalendarEventViewModel(CalendarEvent calendarEvent)
{
    private static readonly Regex UrlRegex = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Id => calendarEvent.Id;

    public string Title => calendarEvent.Title;

    public string Provider => calendarEvent.SourceName;

    public string? Location => calendarEvent.Location;

    public bool HasLocation => !string.IsNullOrWhiteSpace(calendarEvent.Location);

    public Visibility LocationVisibility => HasLocation ? Visibility.Visible : Visibility.Collapsed;

    public string MapsUrl => HasLocation
        ? $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(calendarEvent.Location!)}"
        : string.Empty;

    public string Description => string.IsNullOrWhiteSpace(calendarEvent.Description)
        ? "No extra details were included in this published calendar link."
        : calendarEvent.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(calendarEvent.Description);

    public Visibility DescriptionVisibility => HasDescription ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoDescriptionVisibility => HasDescription ? Visibility.Collapsed : Visibility.Visible;

    public string Organizer => string.IsNullOrWhiteSpace(calendarEvent.Organizer)
        ? string.Empty
        : calendarEvent.Organizer;

    public bool HasOrganizer => !string.IsNullOrWhiteSpace(calendarEvent.Organizer);

    public Visibility OrganizerVisibility => HasOrganizer ? Visibility.Visible : Visibility.Collapsed;

    public string SourceUrl => string.IsNullOrWhiteSpace(calendarEvent.SourceUrl)
        ? string.Empty
        : calendarEvent.SourceUrl;

    public bool HasSourceUrl => !string.IsNullOrWhiteSpace(calendarEvent.SourceUrl);

    public Visibility SourceUrlVisibility => HasSourceUrl ? Visibility.Visible : Visibility.Collapsed;

    public bool IsLocalEvent => string.Equals(calendarEvent.SourceId, LocalCalendarEvent.LocalSourceId, StringComparison.OrdinalIgnoreCase);

    public Visibility DeleteVisibility => IsLocalEvent ? Visibility.Visible : Visibility.Collapsed;

    public string TeamsMeetingUrl => FindTeamsMeetingUrl() ?? string.Empty;

    public bool HasTeamsMeeting => !string.IsNullOrWhiteSpace(TeamsMeetingUrl);

    public Visibility TeamsMeetingVisibility => HasTeamsMeeting ? Visibility.Visible : Visibility.Collapsed;

    public Media.Brush AccentBrush => CalendarSourceColor.GetBrush(calendarEvent.SourceId);

    public string TimeText
    {
        get
        {
            if (calendarEvent.IsAllDay)
            {
                return "All day";
            }

            var start = calendarEvent.Start.LocalDateTime;
            var end = calendarEvent.End.LocalDateTime;

            if (start.Date == end.Date)
            {
                return $"{start.ToString("t", CultureInfo.CurrentCulture)} - {end.ToString("t", CultureInfo.CurrentCulture)}";
            }

            return $"{start.ToString("ddd d MMM t", CultureInfo.CurrentCulture)} - {end.ToString("ddd d MMM t", CultureInfo.CurrentCulture)}";
        }
    }

    private string? FindTeamsMeetingUrl()
    {
        foreach (var value in new[] { calendarEvent.Description, calendarEvent.Location, calendarEvent.SourceUrl })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (Match match in UrlRegex.Matches(value))
            {
                var candidate = WebUtility.HtmlDecode(match.Value.TrimEnd('.', ',', ';', ')', ']', '}'));
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && IsTeamsHost(uri.Host))
                {
                    return uri.ToString();
                }
            }
        }

        return null;
    }

    private static bool IsTeamsHost(string host)
    {
        return host.Equals("teams.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".teams.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("teams.live.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".teams.live.com", StringComparison.OrdinalIgnoreCase);
    }
}
