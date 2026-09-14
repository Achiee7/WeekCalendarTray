using System.Windows;
using System.Windows.Threading;
using WeekCalendarTray.Core;

namespace WeekCalendarTray.UiTests;

internal static class EventDetailsLifecycleTests
{
    public static void Run(MainWindow mainWindow)
    {
        var events = Enumerable.Range(0, 5)
            .Select(index => new CalendarEventViewModel(Program.CreateEvent(
                $"rapid-source-{index}",
                $"Rapid event {index + 1}",
                new DateOnly(2026, 9, 9),
                9 + index)))
            .ToArray();

        try
        {
            mainWindow.Show();
            foreach (var calendarEvent in events)
            {
                mainWindow.OpenEventDetailsCommand.Execute(calendarEvent);
            }

            PumpDispatcher(mainWindow);
            Program.Assert(
                ReferenceEquals(mainWindow.SelectedEvent, events[^1]),
                "five rapid detail selections did not leave the last event in the pane");
            Program.Assert(
                mainWindow.DetailsPaneVisibility == Visibility.Visible,
                "rapid detail selections did not show the details pane");
            Program.Assert(VisibleDetails(mainWindow).Count == 0, "rapid detail selections opened a separate window");

            mainWindow.BackFromDetailsCommand.Execute(null);
            Program.Assert(mainWindow.SelectedEvent is null, "Back retained rapid-selection details");
            Program.Assert(mainWindow.IsDayPaneActive, "Back did not restore the day pane");

            mainWindow.HideCommand.Execute(null);
            PumpDispatcher(mainWindow);
            Program.Assert(!mainWindow.IsVisible, "Hide command did not hide the calendar");
            Program.Assert(VisibleDetails(mainWindow).Count == 0, "hiding the calendar left an orphan detail window");

            mainWindow.Show();
            mainWindow.OpenEventDetailsCommand.Execute(events[0]);
            PumpDispatcher(mainWindow);
            Program.Assert(mainWindow.DetailsPaneVisibility == Visibility.Visible, "details pane did not reopen");
            var targetDate = new DateOnly(2026, 10, 14);
            mainWindow.SelectDateCommand.Execute(new CalendarDayViewModel(
                new CalendarDay(targetDate, targetDate.Day, 42, true, false, false)));
            PumpDispatcher(mainWindow);
            Program.Assert(mainWindow.SelectedEvent is null, "selecting a date left stale event details in the pane");
            Program.Assert(mainWindow.IsDayPaneActive, "selecting a date did not restore the day pane");
            Program.Assert(VisibleDetails(mainWindow).Count == 0, "selecting a date opened a details window");

            mainWindow.OpenEventDetailsCommand.Execute(events[1]);
            PumpDispatcher(mainWindow);
            mainWindow.NextMonthCommand.Execute(null);
            PumpDispatcher(mainWindow);
            Program.Assert(mainWindow.SelectedEvent is null, "month navigation left stale event details in the pane");
            Program.Assert(mainWindow.IsDayPaneActive, "month navigation did not restore the day pane");

            mainWindow.OpenEventDetailsCommand.Execute(events[2]);
            PumpDispatcher(mainWindow);
            mainWindow.ZoomOutCalendarCommand.Execute(null);
            PumpDispatcher(mainWindow);
            Program.Assert(mainWindow.SelectedEvent is null, "calendar-level navigation left stale details in the pane");
            Program.Assert(mainWindow.IsDayPaneActive, "calendar-level navigation did not restore the day pane");
            Program.Assert(VisibleDetails(mainWindow).Count == 0, "pane lifecycle created an EventDetailsWindow");
        }
        finally
        {
            foreach (var detailsWindow in Application.Current.Windows.OfType<EventDetailsWindow>().ToArray())
            {
                detailsWindow.Close();
            }

            mainWindow.Hide();
        }
    }

    private static List<EventDetailsWindow> VisibleDetails(MainWindow owner)
    {
        return Application.Current.Windows
            .OfType<EventDetailsWindow>()
            .Where(window => window.Owner == owner && window.IsVisible)
            .ToList();
    }

    private static void PumpDispatcher(DispatcherObject target)
    {
        target.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
