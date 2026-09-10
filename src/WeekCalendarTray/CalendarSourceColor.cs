using Media = System.Windows.Media;

namespace WeekCalendarTray;

internal static class CalendarSourceColor
{
    private static readonly IReadOnlyList<Media.Brush> Palette = CreatePalette();

    public static Media.Brush GetBrush(string sourceId)
    {
        var hash = 17;
        foreach (var character in sourceId)
        {
            hash = unchecked((hash * 31) + character);
        }

        return Palette[(hash & int.MaxValue) % Palette.Count];
    }

    private static IReadOnlyList<Media.Brush> CreatePalette()
    {
        var colors = new[]
        {
            Media.Color.FromRgb(44, 244, 211),
            Media.Color.FromRgb(66, 133, 244),
            Media.Color.FromRgb(251, 188, 5),
            Media.Color.FromRgb(234, 67, 53),
            Media.Color.FromRgb(52, 168, 83),
            Media.Color.FromRgb(171, 71, 188)
        };

        return colors
            .Select(color =>
            {
                var brush = new Media.SolidColorBrush(color);
                brush.Freeze();
                return (Media.Brush)brush;
            })
            .ToArray();
    }
}
