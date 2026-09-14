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

internal static class DayViewLayouts
{
    public const string List = "List";
    public const string Timeline = "Timeline";

    public static string Parse(string? value)
    {
        return value?.Equals(Timeline, StringComparison.OrdinalIgnoreCase) == true
            ? Timeline
            : List;
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

    public string DayViewLayout { get; set; } = DayViewLayouts.List;

    public bool PrayerTimesEnabled { get; set; }

    public bool PrayerNotificationsEnabled { get; set; }

    public string PrayerLocationName { get; set; } = "Leerdam, NL";

    public double PrayerLatitude { get; set; } = 51.8936d;

    public double PrayerLongitude { get; set; } = 5.0913d;

    // Calendar body width, excluding the prayer toggle and panel chrome.
    public double PopupWidth { get; set; } = PopupSize.DefaultWidth;

    public double PopupHeight { get; set; } = PopupSize.DefaultHeight;
}

/// <summary>
/// Bounds for the user-resizable tray popup. Kept next to the settings model so the
/// persisted range, the window, and the store all clamp against the same numbers.
/// </summary>
internal static class PopupSize
{
    public const double DefaultWidth = 372d;
    public const double DefaultHeight = 560d;

    // The month grid lays out seven day columns plus the week column against the
    // default width, so the popup may grow but never narrow past it.
    public const double MinWidth = 372d;
    public const double MaxWidth = 1400d;

    // Header, month grid, divider, and the bottom action strip still have to fit.
    public const double MinHeight = 470d;
    public const double MaxHeight = 1800d;

    /// <summary>
    /// Returns a finite, in-range size, falling back to the default when a persisted
    /// value is missing (deserializes to 0), corrupt, or not finite.
    /// </summary>
    public static double NormalizeWidth(double value) => Normalize(value, DefaultWidth, MinWidth, MaxWidth);

    public static double NormalizeHeight(double value) => Normalize(value, DefaultHeight, MinHeight, MaxHeight);

    private static double Normalize(double value, double fallback, double min, double max) =>
        !double.IsFinite(value) || value <= 0d ? fallback : Math.Clamp(value, min, max);
}
