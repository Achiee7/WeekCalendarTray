namespace WeekCalendarTray.Core;

public sealed class IcalSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public override string ToString()
    {
        var state = IsEnabled ? "on" : "off";
        return $"{Name} ({state})";
    }
}
