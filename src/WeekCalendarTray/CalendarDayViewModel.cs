using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using WeekCalendarTray.Core;
using Media = System.Windows.Media;

namespace WeekCalendarTray;

public sealed class CalendarDayViewModel(CalendarDay day) : INotifyPropertyChanged
{
    private const int MaxPreviewEvents = 6;
    private IReadOnlyList<CalendarEvent> _events = [];
    private IReadOnlyList<Media.Brush> _eventIndicatorBrushes = [];
    private bool _showEventIndicators;
    private bool _showEventPreviewOnHover;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DateOnly Date => day.Date;

    public int DayNumber => day.DayNumber;

    public int IsoWeekNumber => day.IsoWeekNumber;

    public bool IsCurrentMonth => day.IsCurrentMonth;

    public bool IsToday => day.IsToday;

    public bool IsSelected => day.IsSelected;

    public IReadOnlyList<Media.Brush> EventIndicatorBrushes => _eventIndicatorBrushes;

    public Visibility EventIndicatorVisibility => _showEventIndicators && _events.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string? EventPreviewText
    {
        get
        {
            if (!_showEventPreviewOnHover || _events.Count == 0)
            {
                return null;
            }

            var lines = _events
                .Take(MaxPreviewEvents)
                .Select(FormatPreviewLine)
                .ToList();

            if (_events.Count > MaxPreviewEvents)
            {
                lines.Add($"+ {_events.Count - MaxPreviewEvents} more");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public void UpdateEvents(
        IReadOnlyList<CalendarEvent> events,
        bool showEventIndicators,
        bool showEventPreviewOnHover)
    {
        _events = events;
        _eventIndicatorBrushes = events
            .Select(calendarEvent => calendarEvent.SourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(sourceId => sourceId, StringComparer.OrdinalIgnoreCase)
            .Select(CalendarSourceColor.GetBrush)
            .ToArray();
        _showEventIndicators = showEventIndicators;
        _showEventPreviewOnHover = showEventPreviewOnHover;
        OnPropertyChanged(nameof(EventIndicatorBrushes));
        OnPropertyChanged(nameof(EventIndicatorVisibility));
        OnPropertyChanged(nameof(EventPreviewText));
    }

    private static string FormatPreviewLine(CalendarEvent calendarEvent)
    {
        if (calendarEvent.IsAllDay)
        {
            return $"{calendarEvent.Title} - All day";
        }

        var start = calendarEvent.Start.LocalDateTime;
        var end = calendarEvent.End.LocalDateTime;
        if (start.Date == end.Date)
        {
            return string.Create(
                CultureInfo.CurrentCulture,
                $"{calendarEvent.Title} - {start:t} - {end:t}");
        }

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{calendarEvent.Title} - {start:ddd d MMM t} - {end:ddd d MMM t}");
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
