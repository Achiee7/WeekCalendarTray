using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WeekCalendarTray;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WeekCalendarTray";
    private static bool _testEnabled;

    public static bool IsEnabled()
    {
        if (AppPaths.TestDataDirectory is not null) return _testEnabled;
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return !string.IsNullOrWhiteSpace(key?.GetValue(ValueName) as string);
    }

    public static void SetEnabled(bool enabled)
    {
        // Isolated UI tests must never register their executable in the user's Run key.
        if (AppPaths.TestDataDirectory is not null)
        {
            _testEnabled = enabled;
            return;
        }
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            key.SetValue(ValueName, Quote(GetExecutablePath()), RegistryValueKind.String);
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "WeekCalendarTray.exe");
    }

    private static string Quote(string path)
    {
        return path.StartsWith('"') ? path : $"\"{path}\"";
    }
}
