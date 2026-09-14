using WeekCalendarTray.Core;

namespace WeekCalendarTray;

internal sealed class SyncSettingsStore
{
    public async Task<SyncSettings> LoadAsync()
    {
        var settings = await AtomicJsonFile.LoadAsync(AppPaths.SyncSettingsPath, () => new SyncSettings(),
            ex => AppDiagnostics.Log("Restore settings backup", ex));
        settings.Subscriptions ??= [];
        settings.Subscriptions = settings.Subscriptions.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        settings.SyncPastDays = Math.Clamp(settings.SyncPastDays, 0, 3660);
        settings.SyncFutureDays = Math.Clamp(settings.SyncFutureDays, 1, 3660);
        settings.AcrylicOpacityPercent = Math.Clamp(settings.AcrylicOpacityPercent,
            ThemeManager.MinAcrylicOpacityPercent, ThemeManager.MaxAcrylicOpacityPercent);
        settings.ThemePreference = AppThemePreferences.Parse(settings.ThemePreference).ToString();
        settings.DayViewLayout = DayViewLayouts.Parse(settings.DayViewLayout);
        settings.PopupWidth = PopupSize.NormalizeWidth(settings.PopupWidth);
        settings.PopupHeight = PopupSize.NormalizeHeight(settings.PopupHeight);
        if (!double.IsFinite(settings.PrayerLatitude) || Math.Abs(settings.PrayerLatitude) > 90
            || !double.IsFinite(settings.PrayerLongitude) || Math.Abs(settings.PrayerLongitude) > 180)
            settings.PrayerTimesEnabled = false;
        return settings;
    }

    public async Task SaveAsync(SyncSettings settings)
    {
        settings.DayViewLayout = DayViewLayouts.Parse(settings.DayViewLayout);
        await AtomicJsonFile.SaveAsync(AppPaths.SyncSettingsPath, settings);
    }
}
