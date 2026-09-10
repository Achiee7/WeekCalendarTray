using System.IO;

namespace WeekCalendarTray;

internal static class AppPaths
{
    internal static string? TestDataDirectory { get; set; }

    public static string AppData => TestDataDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WeekCalendarTray");

    public static string SyncSettingsPath => Path.Combine(AppData, "sync-settings.json");

    public static string EventCachePath => Path.Combine(AppData, "events-cache.json");

    public static string LocalEventsPath => Path.Combine(AppData, "local-events.json");

    public static void RemoveLegacyTokenDirectory()
    {
        var path = Path.Combine(AppData, "tokens");
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Legacy cleanup should never stop the tray app from starting.
        }
    }
}
