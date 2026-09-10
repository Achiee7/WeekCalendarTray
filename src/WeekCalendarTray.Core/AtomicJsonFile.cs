using System.Collections.Concurrent;
using System.Text.Json;

namespace WeekCalendarTray.Core;

public static class AtomicJsonFile
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static async Task<T> LoadAsync<T>(string path, Func<T> createDefault, Action<Exception>? onRecovery = null)
    {
        path = Path.GetFullPath(path);
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            try { return await ReadAsync<T>(path).ConfigureAwait(false); }
            catch (FileNotFoundException) when (!File.Exists(path + ".bak")) { return createDefault(); }
            catch (DirectoryNotFoundException) { return createDefault(); }
            catch (Exception ex) when (ex is JsonException or FileNotFoundException)
            {
                // Preserve damaged data before restoring the last complete document.
                var recovered = await ReadAsync<T>(path + ".bak").ConfigureAwait(false);
                if (File.Exists(path))
                    File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                await WriteAsync(path, recovered, keepBackup: false).ConfigureAwait(false);
                onRecovery?.Invoke(ex);
                return recovered;
            }
        }
        finally { gate.Release(); }
    }

    public static async Task SaveAsync<T>(string path, T value)
    {
        path = Path.GetFullPath(path);
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try { await WriteAsync(path, value, keepBackup: true).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private static async Task<T> ReadAsync<T>(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return await JsonSerializer.DeserializeAsync<T>(stream).ConfigureAwait(false)
            ?? throw new JsonException("The stored document is empty.");
    }

    private static async Task WriteAsync<T>(string path, T value, bool keepBackup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (keepBackup && File.Exists(path))
                File.Replace(temporary, path, path + ".bak");
            else
                File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
