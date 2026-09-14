using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WeekCalendarTray.Core;

namespace WeekCalendarTray.UiTests;

internal static class TooltipAndDetailsLayoutTests
{
    public static void Run(MainWindow mainWindow, string artifactDirectory)
    {
        TestBoundedDayPreview(mainWindow);
        TestLongDetailsLayout(mainWindow, artifactDirectory);
    }

    private static void TestBoundedDayPreview(MainWindow mainWindow)
    {
        var workArea = SystemParameters.WorkArea;
        mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        mainWindow.Left = workArea.Left + Math.Min(320d, Math.Max(0d, workArea.Width - mainWindow.Width));
        mainWindow.Top = workArea.Top + 40d;
        mainWindow.Show();
        mainWindow.TodayCommand.Execute(null);
        DrainEventRefresh(mainWindow);
        mainWindow.UpdateLayout();
        mainWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var day = mainWindow.Weeks.SelectMany(week => week.Days).First();
        var events = Enumerable.Range(0, 6)
            .Select(index => Program.CreateEvent(
                $"tooltip-source-{index}",
                $"Very long synthetic preview event {index + 1} with enough text to require wrapping",
                day.Date,
                8 + index))
            .ToArray();
        day.UpdateEvents(events, showEventIndicators: true, showEventPreviewOnHover: true);

        var root = (FrameworkElement)mainWindow.Content;
        var button = Program.Descendants<Button>(root)
            .First(candidate => ReferenceEquals(candidate.DataContext, day));
        var preview = button.ToolTip as ToolTip
            ?? throw new InvalidOperationException("calendar day did not have an explicit ToolTip");

        Program.Assert(ToolTipService.GetInitialShowDelay(button) == 650, "day preview delay changed");
        Program.Assert(ToolTipService.GetShowDuration(button) == 3500, "day preview duration changed");
        Program.Assert(preview.MaxWidth == 280d, "day preview width is not bounded to 280px");
        Program.Assert(preview.MaxHeight == 240d, "day preview height is not bounded to 240px");
        var previewText = (TextBlock)preview.Content;
        Program.Assert(previewText.TextWrapping == TextWrapping.Wrap, "day preview text does not wrap");
        Program.Assert(previewText.TextTrimming != TextTrimming.None, "day preview text does not trim overflow");

        var opening = (ToolTipEventArgs)(Activator.CreateInstance(
            typeof(ToolTipEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [true],
            culture: null) ?? throw new InvalidOperationException("could not create ToolTipEventArgs"));
        Program.InvokePrivate(mainWindow, "DayPreview_Opening", button, opening);
        Program.Assert(!opening.Handled, "bounded day preview was rejected despite available side space");
        Program.Assert(preview.Placement == PlacementMode.Custom, "day preview does not use custom placement");
        var placementCallback = preview.CustomPopupPlacementCallback;
        Program.Assert(placementCallback is not null, "day preview has no placement callback");

        var placements = placementCallback!(
            new Size(280d, 160d),
            new Size(mainWindow.ActualWidth, mainWindow.ActualHeight),
            new Point());
        Program.Assert(placements.Length == 1, "day preview did not return one stable placement");
        var x = placements[0].Point.X;
        Program.Assert(x + 280d <= 0d || x >= mainWindow.ActualWidth,
            "day preview placement overlaps the calendar body");

        Program.SetPrivateField(mainWindow, "_activeDayPreview", preview);
        mainWindow.NextMonthCommand.Execute(null);
        Program.Assert(Program.GetPrivateField<ToolTip>(mainWindow, "_activeDayPreview") is null,
            "calendar navigation did not dismiss the active day preview");
    }

    private static void DrainEventRefresh(MainWindow mainWindow)
    {
        var method = typeof(MainWindow).GetMethod(
            "RefreshEventsFromCacheAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainWindow).FullName, "RefreshEventsFromCacheAsync");
        var refresh = method.Invoke(mainWindow, null) as Task
            ?? throw new InvalidOperationException("RefreshEventsFromCacheAsync did not return a Task");
        if (!refresh.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = refresh.ContinueWith(
                _ => mainWindow.Dispatcher.BeginInvoke((Action)(() => frame.Continue = false)),
                TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }

        refresh.GetAwaiter().GetResult();
    }

    private static void TestLongDetailsLayout(MainWindow mainWindow, string artifactDirectory)
    {
        const string meetingUrl = "https://teams.microsoft.com/l/meetup-join/19%3ameeting_synthetic_test_only";
        const string sourceUrl = "https://example.invalid/calendar/event/this-is-a-deliberately-long-read-only-source-link-for-layout-verification";
        var date = new DateOnly(2026, 9, 9);
        var localTime = date.ToDateTime(new TimeOnly(9, 0));
        var start = new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
        var calendarEvent = new CalendarEvent(
            LocalCalendarEvent.LocalSourceId,
            "Synthetic local calendar with a deliberately long display name",
            "long-details-layout",
            "A deliberately long synthetic event title that wraps across several lines without covering metadata or actions",
            start,
            start.AddHours(2),
            false,
            "Synthetic building, long corridor, room 12345, test city",
            $"{meetingUrl}{Environment.NewLine}{string.Join(' ', Enumerable.Repeat("Long synthetic details remain scrollable.", 30))}",
            "Synthetic Organizer With A Long Display Name",
            sourceUrl);
        var eventViewModel = new CalendarEventViewModel(calendarEvent);

        try
        {
            mainWindow.Show();
            mainWindow.OpenEventDetailsCommand.Execute(eventViewModel);
            mainWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var root = (FrameworkElement)mainWindow.Content;
            Program.MeasureAndArrange(root, mainWindow.Width, mainWindow.Height);

            Program.Assert(mainWindow.DetailsPaneVisibility == Visibility.Visible,
                "long event details were not hosted in the unified pane");
            Program.Assert(mainWindow.SidePanelColumnWidth.Value == 320d,
                "event details pane did not retain the unified 320px width");
            Program.Assert(
                !Application.Current.Windows.OfType<EventDetailsWindow>()
                    .Any(window => window.Owner == mainWindow && window.IsVisible),
                "long event details opened a separate EventDetailsWindow");

            var sourceBox = Program.Descendants<TextBox>(root).Single(textBox => textBox.Text == sourceUrl);
            var detailsScrollViewer = Program.Descendants<ScrollViewer>(root)
                .Single(scrollViewer => Program.Descendants<TextBox>(scrollViewer)
                    .Any(textBox => ReferenceEquals(textBox, sourceBox)));
            var detailsActions = Program.Descendants<WrapPanel>(root)
                .Single(panel => Program.Descendants<Button>(panel)
                    .Any(button => Equals(button.Content, "Navigate")));
            Program.AssertNoOverlap(detailsScrollViewer, detailsActions, root,
                "details scroll area", "details actions");

            var actionButtons = Program.Descendants<Button>(detailsActions)
                .Where(button => button.Visibility == Visibility.Visible)
                .ToArray();
            Program.Assert(actionButtons.Length == 4, "long synthetic details did not expose all four pane actions");
            for (var first = 0; first < actionButtons.Length; first++)
            {
                for (var second = first + 1; second < actionButtons.Length; second++)
                {
                    Program.AssertNoOverlap(actionButtons[first], actionButtons[second], root,
                        $"details action {first}", $"details action {second}");
                }
            }

            Program.Assert(sourceBox.IsReadOnly, "event source URL is not read-only");
            Program.Assert(sourceBox.TextWrapping == TextWrapping.Wrap, "event source URL does not wrap within the pane");
            Program.RenderWindowContent(mainWindow,
                Path.Combine(artifactDirectory, "MainWindow-details-pane-long-content.png"));
        }
        finally
        {
            mainWindow.BackFromDetailsCommand.Execute(null);
        }
    }
}
