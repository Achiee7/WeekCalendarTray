using WeekCalendarTray.Core;

namespace WeekCalendarTray;

internal enum AppThemePreference
{
    System,
    Light,
    Dark
}

internal static class AppThemePreferences
{
    public static AppThemePreference Parse(string? value)
    {
        return Enum.TryParse<AppThemePreference>(value, ignoreCase: true, out var preference)
            && Enum.IsDefined(preference)
            ? preference
            : AppThemePreference.System;
    }
}

internal sealed class SyncSettings
{
    public List<IcalSubscription> Subscriptions { get; set; } = [];

    public int SyncPastDays { get; set; } = 30;

    public int SyncFutureDays { get; set; } = 180;

    public bool ShowEventIndicators { get; set; } = true;

    public bool ShowEventPreviewOnHover { get; set; } = true;

    public bool AcrylicEnabled { get; set; }

    public int AcrylicOpacityPercent { get; set; } = 75;

    public string ThemePreference { get; set; } = nameof(AppThemePreference.System);

    public bool PrayerTimesEnabled { get; set; }

    public bool PrayerNotificationsEnabled { get; set; }

    public string PrayerLocationName { get; set; } = "Leerdam, NL";

    public double PrayerLatitude { get; set; } = 51.8936d;

    public double PrayerLongitude { get; set; } = 5.0913d;
}
