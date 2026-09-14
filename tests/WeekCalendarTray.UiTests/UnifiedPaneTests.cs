using System.IO;
using System.Windows;
using System.Windows.Controls;
using WeekCalendarTray.Core;

namespace WeekCalendarTray.UiTests;

internal static class UnifiedPaneTests
{
    public static void Run(MainWindow mainWindow, string artifactDirectory)
    {
        var originalBaseWidth = GetBaseWidth(mainWindow);
        var originalHeight = mainWindow.Height;

        try
        {
            mainWindow.Show();
            Pump(mainWindow);

            TestInitialMonthAndToday(mainWindow);
            TestUnifiedPaneModes(mainWindow);
            TestWeekNavigationAndResizing(mainWindow, artifactDirectory);
        }
        finally
        {
            mainWindow.ShowMonthViewCommand.Execute(null);
            if (mainWindow.SidePanelVisibility == Visibility.Visible)
            {
                mainWindow.ToggleSidePanelCommand.Execute(null);
            }

            Program.SetPrivateField(mainWindow, "_baseWindowWidth", originalBaseWidth);
            Program.InvokePrivate(mainWindow, "UpdateSideLayout", false);
            mainWindow.Height = originalHeight;
        }
    }

    private static void TestInitialMonthAndToday(MainWindow mainWindow)
    {
        Program.Assert(mainWindow.IsMonthView, "initial calendar opening did not default to month view");
        Program.Assert(mainWindow.MonthViewVisibility == Visibility.Visible, "initial month grid was not visible");

        mainWindow.NextMonthCommand.Execute(null);
        mainWindow.TodayCommand.Execute(null);
        Program.Assert(
            mainWindow.SelectedDate == DateOnly.FromDateTime(DateTime.Now),
            "Today did not select the current date in month view");
        Program.Assert(mainWindow.IsMonthView, "Today changed month view into another display mode");

        mainWindow.ShowWeekViewCommand.Execute(null);
        mainWindow.NextMonthCommand.Execute(null);
        mainWindow.TodayCommand.Execute(null);
        Program.Assert(
            mainWindow.SelectedDate == DateOnly.FromDateTime(DateTime.Now),
            "Today did not select the current date in week view");
        Program.Assert(mainWindow.IsWeekView, "Today changed week view into another display mode");

        mainWindow.ShowMonthViewCommand.Execute(null);
    }

    private static void TestUnifiedPaneModes(MainWindow mainWindow)
    {
        Program.SetPrivateField(mainWindow, "_prayerTimesEnabled", false);
        Program.InvokePrivate(mainWindow, "UpdateSideLayout", false);

        Program.Assert(mainWindow.SideToggleColumnWidth.Value == 28d, "side-pane toggle chrome is not always present");
        Program.Assert(mainWindow.SidePanelVisibility == Visibility.Collapsed, "side pane did not start collapsed");
        Program.Assert(mainWindow.SidePanelColumnWidth.Value == 0d, "collapsed side pane retained width");

        var collapsedWidth = mainWindow.Width;
        mainWindow.ToggleSidePanelCommand.Execute(null);
        Program.Assert(mainWindow.SidePanelVisibility == Visibility.Visible, "side pane did not expand");
        Program.Assert(mainWindow.SidePanelColumnWidth.Value == 320d, "expanded side pane was not 320px wide");
        Program.Assert(Math.Abs(mainWindow.Width - collapsedWidth - 320d) < 0.1d, "side pane expanded by the wrong width");
        Program.Assert(
            Math.Abs(mainWindow.MinWidth - (PopupSize.MinWidth + 348d)) < 0.1d,
            "expanded pane minimum width did not include the 28px toggle and 320px pane");
        Program.Assert(mainWindow.IsDayPaneActive, "expanded side pane did not default to day view");
        Program.Assert(mainWindow.DayPaneVisibility == Visibility.Visible, "day pane was not visible after expansion");
        Program.Assert(mainWindow.PrayerFeatureVisibility == Visibility.Collapsed, "prayer tab remained visible while disabled");
        Program.Assert(mainWindow.DayListVisibility == Visibility.Visible, "list day-view setting did not show the list");
        Program.Assert(mainWindow.DayTimelineVisibility == Visibility.Collapsed, "list day-view setting retained the timeline");

        Program.SetPrivateField(mainWindow, "_dayViewLayout", DayViewLayouts.Timeline);
        Program.Assert(mainWindow.DayListVisibility == Visibility.Collapsed, "timeline day-view setting retained the list");
        Program.Assert(mainWindow.DayTimelineVisibility == Visibility.Visible, "timeline day-view setting did not show the timeline");
        Program.SetPrivateField(mainWindow, "_dayViewLayout", DayViewLayouts.List);

        mainWindow.ShowPrayerPaneCommand.Execute(null);
        Program.Assert(mainWindow.IsDayPaneActive, "disabled prayer command displaced the day pane");
        Program.Assert(mainWindow.DayPaneVisibility == Visibility.Visible, "disabled prayer command hid the day pane");

        var firstEvent = mainWindow.SelectedDayEvents.FirstOrDefault()
            ?? new CalendarEventViewModel(Program.CreateEvent(
                "unified-pane",
                "Unified pane details",
                mainWindow.SelectedDate,
                10));
        mainWindow.OpenEventDetailsCommand.Execute(firstEvent);
        Program.Assert(ReferenceEquals(mainWindow.SelectedEvent, firstEvent), "details pane did not select the event");
        Program.Assert(mainWindow.DetailsPaneVisibility == Visibility.Visible, "event details did not appear in the side pane");
        Program.Assert(mainWindow.SideTabsVisibility == Visibility.Collapsed, "pane tabs remained above event details");
        AssertNoDetailsWindow(mainWindow, "opening details from the calendar");

        mainWindow.BackFromDetailsCommand.Execute(null);
        Program.Assert(mainWindow.SelectedEvent is null, "Back retained the selected event");
        Program.Assert(mainWindow.IsDayPaneActive, "Back did not restore the day pane");
        Program.Assert(mainWindow.DayPaneVisibility == Visibility.Visible, "Back left the day pane hidden");

        mainWindow.OpenEventDetailsCommand.Execute(firstEvent);
        var targetDate = mainWindow.SelectedDate.AddDays(1);
        mainWindow.SelectDateCommand.Execute(new CalendarDayViewModel(
            new CalendarDay(targetDate, targetDate.Day, 1, true, false, false)));
        Program.Assert(mainWindow.SelectedEvent is null, "date navigation retained stale event details");
        Program.Assert(mainWindow.IsDayPaneActive, "date navigation did not restore the day pane");
        AssertNoDetailsWindow(mainWindow, "date navigation");

        Program.SetPrivateField(mainWindow, "_prayerTimesEnabled", true);
        Program.InvokePrivate(mainWindow, "UpdateSideLayout", false);
        mainWindow.ShowPrayerPaneCommand.Execute(null);
        Program.Assert(mainWindow.PrayerFeatureVisibility == Visibility.Visible, "enabled prayer tab was hidden");
        Program.Assert(mainWindow.IsPrayerPaneActive, "prayer tab did not activate the prayer pane");
        Program.Assert(mainWindow.PrayerPaneVisibility == Visibility.Visible, "active prayer pane was not visible");

        mainWindow.ShowDayPaneCommand.Execute(null);
        Program.Assert(mainWindow.IsDayPaneActive, "day tab did not return from prayer times");
        mainWindow.ToggleSidePanelCommand.Execute(null);
        Program.Assert(mainWindow.SidePanelVisibility == Visibility.Collapsed, "side pane did not collapse");
    }

    private static void TestWeekNavigationAndResizing(MainWindow mainWindow, string artifactDirectory)
    {
        mainWindow.ShowWeekViewCommand.Execute(null);
        var selected = mainWindow.SelectedDate;
        mainWindow.NextMonthCommand.Execute(null);
        Program.Assert(mainWindow.SelectedDate == selected.AddDays(7), "week navigation did not advance seven days");
        mainWindow.PreviousMonthCommand.Execute(null);
        Program.Assert(mainWindow.SelectedDate == selected, "week navigation did not move back seven days");
        Program.Assert(mainWindow.PreviousStepToolTip == "Previous week", "week view kept the month navigation tooltip");
        Program.Assert(mainWindow.NextStepToolTip == "Next week", "week view kept the month navigation tooltip");

        var start = StartOfWeek(mainWindow.SelectedDate);
        var events = CreatePopulatedWeek(start);
        mainWindow.WeekTimeline.StartDate = start;
        mainWindow.WeekTimeline.DayCount = 7;
        mainWindow.WeekTimeline.Events = events;

        var root = (FrameworkElement)mainWindow.Content;
        mainWindow.Width = 720d;
        mainWindow.Height = 640d;
        Program.MeasureAndArrange(root, mainWindow.Width, mainWindow.Height);
        mainWindow.WeekTimeline.Refresh();
        Program.MeasureAndArrange(root, mainWindow.Width, mainWindow.Height);
        var compactWidth = mainWindow.WeekTimeline.ActualWidth;
        Program.Assert(compactWidth > 0d, "compact week timeline had no usable width");
        ScrollToMorning(mainWindow.WeekTimeline);
        Program.RenderWindowContent(
            mainWindow,
            Path.Combine(artifactDirectory, "MainWindow-week-populated-compact.png"));

        mainWindow.Width = 1040d;
        mainWindow.Height = 760d;
        Program.MeasureAndArrange(root, mainWindow.Width, mainWindow.Height);
        mainWindow.WeekTimeline.Refresh();
        Program.MeasureAndArrange(root, mainWindow.Width, mainWindow.Height);
        Program.Assert(
            mainWindow.WeekTimeline.ActualWidth > compactWidth,
            "week timeline did not grow when the resizable calendar widened");
        ScrollToMorning(mainWindow.WeekTimeline);
        Program.RenderWindowContent(
            mainWindow,
            Path.Combine(artifactDirectory, "MainWindow-week-populated-wide.png"));

        Program.Assert(
            mainWindow.WeekTimeline.Events.Any(calendarEvent => calendarEvent.IsAllDay),
            "populated week integration fixture lost its all-day event");
        Program.Assert(
            mainWindow.WeekTimeline.Events.Count(calendarEvent =>
                calendarEvent.OccursOn(start.AddDays(2))) >= 3,
            "populated week integration fixture lost its overlapping events");

        var eventButton = Program.Descendants<Button>(mainWindow.WeekTimeline)
            .First(button => button.Tag is CalendarEventViewModel item && item.Id == "week-overlap-a");
        eventButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Program.Assert(mainWindow.SelectedDate == start.AddDays(2), "week event selection did not select the occurrence day");
        Program.Assert(mainWindow.DetailsPaneVisibility == Visibility.Visible, "week click did not open inline details");
        mainWindow.BackFromDetailsCommand.Execute(null);
        mainWindow.ShowMonthViewCommand.Execute(null);
        Program.SetPrivateField(mainWindow, "_baseWindowWidth", PopupSize.DefaultWidth);
        Program.InvokePrivate(mainWindow, "UpdateSideLayout", false);
        mainWindow.SelectedDayEvents.Clear();
        foreach (var item in events.Where(item => item.OccursOn(start.AddDays(2))))
            mainWindow.SelectedDayEvents.Add(item);
        Program.SetPrivateField(mainWindow, "_dayTimelineEvents", mainWindow.SelectedDayEvents.ToList());
        Program.RaisePropertyChanged(mainWindow, "AgendaEmptyVisibility");
        Program.RaisePropertyChanged(mainWindow, "DayListEmptyVisibility");
        Program.RenderWindowContent(mainWindow, Path.Combine(artifactDirectory, "MainWindow-day-list.png"));
        Program.SetPrivateField(mainWindow, "_dayViewLayout", DayViewLayouts.Timeline);
        Program.RaisePropertyChanged(mainWindow, "DayListVisibility");
        Program.RaisePropertyChanged(mainWindow, "DayTimelineVisibility");
        Program.RaisePropertyChanged(mainWindow, "DayListEmptyVisibility");
        Program.InvokePrivate(mainWindow, "RefreshTimelineViews");
        Program.MeasureAndArrange(root, mainWindow.Width, mainWindow.Height);
        ScrollToMorning(mainWindow.DayTimeline);
        Program.RenderWindowContent(mainWindow, Path.Combine(artifactDirectory, "MainWindow-day-timeline.png"));
        Program.SetPrivateField(mainWindow, "_dayViewLayout", DayViewLayouts.List);
        Program.RaisePropertyChanged(mainWindow, "DayListVisibility");
        Program.RaisePropertyChanged(mainWindow, "DayTimelineVisibility");
    }

    private static void ScrollToMorning(TimelineView timeline)
    {
        Program.Descendants<ScrollViewer>(timeline)
            .Single(scroll => scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Visible)
            .ScrollToVerticalOffset(8 * 60);
        timeline.UpdateLayout();
    }

    private static IReadOnlyList<CalendarEventViewModel> CreatePopulatedWeek(DateOnly start)
    {
        var overlappingDate = start.AddDays(2);
        return
        [
            CreateEvent("week-all-day", "All-day milestone", start.AddDays(1), 0, 24, isAllDay: true),
            CreateEvent("week-overlap-a", "Planning", overlappingDate, 9, 2),
            CreateEvent("week-overlap-b", "Review", overlappingDate, 9.5, 1.5),
            CreateEvent("week-overlap-c", "Office hours", overlappingDate, 10, 2),
            CreateEvent("week-late", "Evening class", start.AddDays(4), 19, 2)
        ];
    }

    private static CalendarEventViewModel CreateEvent(
        string id,
        string title,
        DateOnly date,
        double startHour,
        double durationHours,
        bool isAllDay = false)
    {
        var localStart = date.ToDateTime(TimeOnly.MinValue).AddHours(startHour);
        var start = new DateTimeOffset(localStart, TimeZoneInfo.Local.GetUtcOffset(localStart));
        return new CalendarEventViewModel(new CalendarEvent(
            "unified-week",
            "Unified week fixture",
            id,
            title,
            start,
            start.AddHours(durationHours),
            isAllDay,
            "Synthetic room",
            "Synthetic details",
            "Synthetic organizer",
            "https://example.invalid/week-event"));
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    private static double GetBaseWidth(MainWindow mainWindow)
    {
        var field = typeof(MainWindow).GetField(
            "_baseWindowWidth",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(MainWindow).FullName, "_baseWindowWidth");
        return (double)field.GetValue(mainWindow)!;
    }

    private static void AssertNoDetailsWindow(MainWindow mainWindow, string action)
    {
        var visible = Application.Current.Windows
            .OfType<EventDetailsWindow>()
            .Any(window => window.Owner == mainWindow && window.IsVisible);
        Program.Assert(!visible, $"{action} created a separate EventDetailsWindow");
    }

    private static void Pump(FrameworkElement target)
    {
        target.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
