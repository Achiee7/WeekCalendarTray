namespace WeekCalendarTray;

internal sealed record TimelineEntry(CalendarEventViewModel Event, int Day, double StartMinute,
    double EndMinute, int Column, int ColumnCount);

internal static class TimelineLayout
{
    // Reserve a small hit target for short events, also in collision calculations.
    internal const double MinimumMinutes = 20;

    public static IReadOnlyList<TimelineEntry> Create(
        DateOnly startDate, int dayCount, IEnumerable<CalendarEventViewModel> events)
    {
        var result = new List<TimelineEntry>();
        var timed = events.Where(e => !e.IsAllDay)
            .DistinctBy(e => (e.SourceId, e.Id, e.Start, e.End)).ToArray();
        for (var day = 0; day < dayCount; day++)
        {
            var date = startDate.AddDays(day);
            var midnight = date.ToDateTime(TimeOnly.MinValue);
            var entries = timed.Where(e => e.OccursOn(date)).Select(e =>
            {
                var start = Math.Clamp((e.Start.LocalDateTime - midnight).TotalMinutes, 0, 1440);
                var end = Math.Clamp((e.End.LocalDateTime - midnight).TotalMinutes, 0, 1440);
                start = Math.Min(start, 1440 - MinimumMinutes);
                return new TimelineEntry(e, day, start, Math.Min(1440, Math.Max(start + MinimumMinutes, end)), 0, 1);
            }).OrderBy(e => e.StartMinute).ThenByDescending(e => e.EndMinute).ThenBy(e => e.Event.Title).ToArray();

            // Connected overlap groups share columns; consecutive events reuse the full day width.
            for (var first = 0; first < entries.Length;)
            {
                var endIndex = first + 1;
                var groupEnd = entries[first].EndMinute;
                while (endIndex < entries.Length && entries[endIndex].StartMinute < groupEnd)
                {
                    groupEnd = Math.Max(groupEnd, entries[endIndex].EndMinute);
                    endIndex++;
                }
                var columnEnds = new List<double>();
                var group = new List<TimelineEntry>();
                for (var i = first; i < endIndex; i++)
                {
                    var column = columnEnds.FindIndex(end => end <= entries[i].StartMinute);
                    if (column < 0) { column = columnEnds.Count; columnEnds.Add(0); }
                    columnEnds[column] = entries[i].EndMinute;
                    group.Add(entries[i] with { Column = column });
                }
                result.AddRange(group.Select(e => e with { ColumnCount = columnEnds.Count }));
                first = endIndex;
            }
        }
        return result;
    }
}
