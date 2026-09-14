using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WeekCalendarTray.Core;

namespace WeekCalendarTray.UiTests;

internal static class TimelineViewTests
{
    public static void Run(string artifactDirectory)
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        CalendarEventViewModel Event(string id, int minute, int duration, bool allDay = false) => new(new CalendarEvent(
            id == "b" ? "second" : "first", "Sample calendar", id, "Planning session " + id,
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute)),
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute + duration)),
            allDay, "Meeting room 2", "Synthetic test event", null, null));
        var events = new[] { Event("a", 540, 120), Event("b", 570, 60), Event("c", 660, 60),
            Event("overnight", -60, 120), Event("zero", 1439, 0), Event("all", 0, 1440, true) };
        var entries = TimelineLayout.Create(date, 1, events.Concat(events));
        Assert(entries.Count == 5, "duplicates/all-day events leaked into timed layout");
        var a = entries.Single(e => e.Event.Id == "a");
        var b = entries.Single(e => e.Event.Id == "b");
        var c = entries.Single(e => e.Event.Id == "c");
        Assert(a.ColumnCount == 2 && b.ColumnCount == 2 && a.Column != b.Column, "overlapping events did not split columns");
        Assert(c.ColumnCount == 1 && c.Column == 0, "adjacent event did not reclaim full width");
        Assert(entries.Single(e => e.Event.Id == "overnight").StartMinute == 0, "overnight event was not clipped to midnight");
        Assert(entries.All(e => e.StartMinute >= 0 && e.EndMinute <= 1440 && e.EndMinute > e.StartMinute), "timeline has out-of-day bounds");
        Assert(TimelineLayout.Create(date.AddDays(-1), 2, events).Count(e => e.Event.Id == "overnight") == 2,
            "overnight event was not split across days");

        var view = new TimelineView { StartDate = date, DayCount = 7, Events = events };
        var border = new Border { Child = view, Padding = new Thickness(8) };
        border.SetResourceReference(Border.BackgroundProperty, "WindowSurfaceBrush");
        var window = new Window { Content = border, Width = 1000, Height = 700 };
        try
        {
            Arrange(window, view);
            Assert(view.HasCurrentTimeMarker, "today timeline lacks current-time line");
            var rendered = view.RenderCount;
            var eventControl = Descendants<Button>(view).First(x => x.Tag is CalendarEventViewModel);
            for (var i = 0; i < 100; i++) view.Refresh();
            Assert(view.RenderCount == rendered, "unchanged timeline repeatedly rebuilt its visual tree");
            Assert(ReferenceEquals(eventControl, Descendants<Button>(view).First(x => x.Tag is CalendarEventViewModel)),
                "unchanged timeline discarded event controls");
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) view.RefreshCurrentTime();
            var markerBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert(markerBytes < 64000, "time marker allocated excessive objects on repeated updates");
            Console.WriteLine($"Time marker: {markerBytes} bytes allocated across 1000 updates; unchanged timeline renders skipped.");
            var wide = view.DayWidth;
            view.ScrollToCurrentTime();
            Pump();
            Program.RenderWindowContent(window, Path.Combine(artifactDirectory, "Timeline-week-wide.png"));
            window.Width = 440;
            Arrange(window, view);
            Assert(view.DayWidth < wide && view.DayWidth >= 89, "week columns did not scale to their readable minimum");
            Program.RenderWindowContent(window, Path.Combine(artifactDirectory, "Timeline-week-narrow.png"));
            view.DayCount = 1;
            window.Width = 320;
            Arrange(window, view);
            view.ScrollToCurrentTime();
            Pump();
            Program.RenderWindowContent(window, Path.Combine(artifactDirectory, "Timeline-day.png"));
            var selected = false;
            view.EventSelected += (_, _) => selected = true;
            var button = Descendants<Button>(view).First(x => x.Tag is CalendarEventViewModel);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(selected, "timeline event button did not raise selection");
            var scroller = Descendants<ScrollViewer>(view).Single(x => x.VerticalScrollBarVisibility == ScrollBarVisibility.Visible);
            scroller.ScrollToBottom();
            Pump();
            Assert(scroller.VerticalOffset > 600, "cannot scroll to end of full 24-hour timeline");
            var offset = scroller.VerticalOffset;
            view.RefreshCurrentTime();
            Assert(scroller.VerticalOffset == offset, "time update reset the user's scroll position");
            view.StartDate = date.AddDays(1);
            view.Refresh();
            Assert(!view.HasCurrentTimeMarker, "current-time marker appears on another day");
            view.ReleaseVisuals();
            Assert(view.Entries.Count == 0 && !Descendants<Button>(view).Any(x => x.Tag is CalendarEventViewModel),
                "hidden timeline retained event visuals");
            view.Refresh();
            Assert(view.RenderCount > rendered, "released timeline did not rebuild when requested");
        }
        finally { window.Close(); }
    }

    private static void Arrange(Window window, TimelineView view)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new System.Windows.Size(window.Width, window.Height));
        root.Arrange(new Rect(0, 0, window.Width, window.Height));
        view.Refresh();
        root.UpdateLayout();
        Pump();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
