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
            var details = VisibleDetails(mainWindow);
            Program.Assert(details.Count == 1, $"five rapid detail opens created {details.Count} windows");
            Program.Assert(
                ReferenceEquals(details[0].DataContext, events[^1]),
                "singleton detail window did not update to the last selected event");

            mainWindow.HideCommand.Execute(null);
            PumpDispatcher(mainWindow);
            Program.Assert(!mainWindow.IsVisible, "Hide command did not hide the calendar");
            Program.Assert(VisibleDetails(mainWindow).Count == 0, "hiding the calendar left an orphan detail window");

            mainWindow.Show();
            mainWindow.OpenEventDetailsCommand.Execute(events[0]);
            PumpDispatcher(mainWindow);
            Program.Assert(VisibleDetails(mainWindow).Count == 1, "detail did not reopen before date selection");
            var targetDate = new DateOnly(2026, 10, 14);
            mainWindow.SelectDateCommand.Execute(new CalendarDayViewModel(
                new CalendarDay(targetDate, targetDate.Day, 42, true, false, false)));
            PumpDispatcher(mainWindow);
            Program.Assert(VisibleDetails(mainWindow).Count == 0, "selecting a date left stale event details open");

            mainWindow.OpenEventDetailsCommand.Execute(events[1]);
            PumpDispatcher(mainWindow);
            mainWindow.NextMonthCommand.Execute(null);
            PumpDispatcher(mainWindow);
            Program.Assert(VisibleDetails(mainWindow).Count == 0, "month navigation left stale event details open");

            mainWindow.OpenEventDetailsCommand.Execute(events[2]);
            PumpDispatcher(mainWindow);
            mainWindow.ZoomOutCalendarCommand.Execute(null);
            PumpDispatcher(mainWindow);
            Program.Assert(VisibleDetails(mainWindow).Count == 0, "calendar-level navigation left stale event details open");
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
