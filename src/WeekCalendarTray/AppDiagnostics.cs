using System.Diagnostics;
using System.IO;

namespace WeekCalendarTray;

internal static class AppDiagnostics
{
    private static readonly object Gate = new();

    public static void Log(string operation, Exception ex)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeekCalendarTray", "Logs");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "diagnostics.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                // Messages can contain private ICS URLs or event details. Log only metadata.
                var methods = new StackTrace(ex, false).GetFrames()
                    .Select(frame => frame.GetMethod())
                    .Where(method => method is not null)
                    .Select(method => $"{method!.DeclaringType?.FullName}.{method.Name}");
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} | {operation} | {ex.GetType().FullName} | 0x{ex.HResult:X8}\n{string.Join("\n", methods)}\n");
            }
        }
        catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Logging must not become another cause of failure.
        }
    }

    public static async Task RunAsync(string operation, Func<Task> action, Action? onFailure = null)
    {
        try { await action(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log(operation, ex);
            onFailure?.Invoke();
        }
    }
}
