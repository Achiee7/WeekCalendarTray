using WeekCalendarTray.Core;

internal static class PersistenceTests
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WeekCalendarTray.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var missing = await AtomicJsonFile.LoadAsync(path, () => new Document(0, "default"));
            Require(missing.Generation == 0, "Missing settings must use defaults.");

            await AtomicJsonFile.SaveAsync(path, new Document(1, new string('a', 10000)));
            await AtomicJsonFile.SaveAsync(path, new Document(2, new string('b', 10000)));
            await File.WriteAllTextAsync(path, "{\"Generation\":");
            var recovered = await AtomicJsonFile.LoadAsync(path, () => new Document(0, "default"));
            Require(recovered.Generation == 1 && recovered.Payload.Length == 10000, "A damaged save must recover its last complete backup.");
            Require(Directory.GetFiles(directory, "*.corrupt-*").Length == 1, "Damaged data must be preserved for recovery.");

            await Task.WhenAll(Enumerable.Range(3, 24).Select(async generation =>
            {
                await AtomicJsonFile.SaveAsync(path, new Document(generation, new string('x', generation * 100)));
                var read = await AtomicJsonFile.LoadAsync(path, () => new Document(0, ""));
                Require(read.Payload.Length == read.Generation * 100, "Concurrent readers must see one complete save.");
            }));
            Require(Directory.GetFiles(directory, "*.tmp").Length == 0, "Successful saves must not leave temporary files.");

            var before = await File.ReadAllTextAsync(path);
            try { await AtomicJsonFile.SaveAsync(path, new { Invalid = double.NaN }); }
            catch (ArgumentException) { }
            Require(await File.ReadAllTextAsync(path) == before, "Failed serialization must leave the existing settings intact.");

            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var rejected = false;
                try { await AtomicJsonFile.LoadAsync(path, () => new Document(0, "default")); }
                catch (IOException) { rejected = true; }
                Require(rejected, "An inaccessible file must not be mistaken for missing settings.");
            }
            Require(await File.ReadAllTextAsync(path) == before, "A failed read must preserve settings.");

            var broken = Path.Combine(directory, "broken.json");
            await File.WriteAllTextAsync(broken, "{broken");
            var failed = false;
            try { await AtomicJsonFile.LoadAsync(broken, () => new Document(0, "default")); }
            catch (IOException) { failed = true; }
            Require(failed && await File.ReadAllTextAsync(broken) == "{broken", "Unrecoverable data must not silently reset.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public sealed record Document(int Generation, string Payload);
}
