using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeekCalendarTray.Core;

namespace WeekCalendarTray;

internal sealed class PrayerLocationResolver
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public PrayerLocationResolver()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WeekCalendarTray/1.0");
    }

    public async Task<PrayerTimesLocation?> ResolveAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var url = "https://nominatim.openstreetmap.org/search?format=json&limit=1&q="
            + Uri.EscapeDataString(query.Trim());
        await using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
        var results = await JsonSerializer.DeserializeAsync<List<NominatimLocation>>(stream, cancellationToken: cancellationToken);
        var result = results?.FirstOrDefault();
        if (result is null
            || !double.TryParse(result.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(result.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
        {
            return null;
        }

        return new PrayerTimesLocation(GetShortDisplayName(result.DisplayName, query), latitude, longitude);
    }

    private static string GetShortDisplayName(string? displayName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return fallback.Trim();
        }

        var parts = displayName
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(3)
            .ToList();

        return parts.Count == 0
            ? fallback.Trim()
            : string.Join(", ", parts);
    }

    private sealed class NominatimLocation
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("lat")]
        public string? Latitude { get; set; }

        [JsonPropertyName("lon")]
        public string? Longitude { get; set; }
    }
}
