namespace WeekCalendarTray;

internal sealed class SyncResult
{
    public int Events { get; set; }

    public int Calendars { get; set; }

    public List<string> Messages { get; } = [];

    public bool HasErrors { get; set; }

    public string Summary
    {
        get
        {
            if (Messages.Count > 0)
            {
                return string.Join(" ", Messages);
            }

            return $"Synced {Events} events from {Calendars} calendar link(s).";
        }
    }
}
