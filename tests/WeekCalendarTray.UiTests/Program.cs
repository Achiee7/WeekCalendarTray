using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WeekCalendarTray.Core;
using Media = System.Windows.Media;
using WpfApplication = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace WeekCalendarTray.UiTests;

internal static class Program
{
    private const double GeometryTolerance = 0.1d;
    private static readonly DateOnly SampleDate = new(2026, 9, 9);

    [STAThread]
    private static int Main(string[] args)
    {
        var testDataDirectory = Path.Combine(
            Path.GetTempPath(),
            "WeekCalendarTray.UiTests",
            Guid.NewGuid().ToString("N"));
        var artifactDirectory = GetArtifactDirectory(args);
        var nativeVisualRequested = args.Contains("--native-visual", StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(testDataDirectory);
        Directory.CreateDirectory(artifactDirectory);

        AppPaths.TestDataDirectory = testDataDirectory;
        var application = new WpfApplication
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };

        MainWindow? mainWindow = null;
        SettingsWindow? settingsWindow = null;
        try
        {
            ThemeManager.Initialize();
            WaitForThemeInitialization();
            TestCalendarDayColorsAndToggles();
            ThemeRegressionTests.Run();
            if (args.Contains("--native-first-open", StringComparer.OrdinalIgnoreCase))
                NativeAcrylicVisualTests.RunFirstOpen(artifactDirectory);

            using var coordinator = new CalendarSyncCoordinator();
            mainWindow = new MainWindow(coordinator);
            TestMeasuredDayStripe(mainWindow);
            PopulateMainWindow(mainWindow);

            Assert(mainWindow.DayButtonContent == "Day", "month view did not label the toggle Day");
            mainWindow.DayOrTodayCommand.Execute(null);
            Assert(mainWindow.IsDayView, "day toggle did not select day view");
            Assert(mainWindow.DayButtonContent == "Today", "day view did not relabel the toggle Today");
            Assert(
                mainWindow.NavigationTitleRowHeight.Value == 0d,
                "day view kept the duplicate title row");
            Assert(mainWindow.DayViewVisibility == Visibility.Visible, "day view did not become visible");
            Assert(mainWindow.CalendarGridRowHeight.Value == 0d, "day view retained the month grid row");
            Assert(mainWindow.WeekdayHeaderRowHeight.Value == 0d, "day view retained the weekday header row");
            Assert(mainWindow.PreviousStepToolTip == "Previous day", "day view kept the month navigation tooltip");
            var dayBeforeStep = mainWindow.SelectedDate;
            mainWindow.NextMonthCommand.Execute(null);
            Assert(
                mainWindow.SelectedDate == dayBeforeStep.AddDays(1),
                "day view navigation did not advance a single day");
            mainWindow.PreviousMonthCommand.Execute(null);
            Assert(mainWindow.SelectedDate == dayBeforeStep, "day view navigation did not step back a single day");
            RenderWindowContent(
                mainWindow,
                Path.Combine(artifactDirectory, "MainWindow-day-dark.png"));
            // A second press, now labelled Today, must jump to today rather than re-enter Day view.
            mainWindow.DayOrTodayCommand.Execute(null);
            Assert(
                mainWindow.SelectedDate == DateOnly.FromDateTime(DateTime.Now),
                "second day-toggle press did not jump to today");
            Assert(mainWindow.IsDayView, "jumping to today left day view");

            mainWindow.ShowMonthViewCommand.Execute(null);
            Assert(mainWindow.IsMonthView, "month toggle did not restore month view");
            Assert(mainWindow.DayButtonContent == "Day", "month view did not restore the Day label");
            Assert(
                mainWindow.NavigationTitleRowHeight.Value == 40d,
                "month view lost its title row");
            Assert(mainWindow.WeekdayHeaderRowHeight.Value == 30d, "month view lost the weekday header row");
            Assert(mainWindow.PreviousStepToolTip == "Previous month", "month view kept the day navigation tooltip");

            AssertClose(372d, mainWindow.Width, "normal main-window width");
            ApplyTheme(light: false, acrylic: false);
            RenderWindowContent(
                mainWindow,
                Path.Combine(artifactDirectory, "MainWindow-normal-dark.png"));

            SetPrivateField(mainWindow, "_prayerTimesEnabled", true);
            SetPrivateField(
                mainWindow,
                "_prayerLocation",
                new PrayerTimesLocation(
                    "Synthetic location with a deliberately long display name for layout verification",
                    51.8936d,
                    5.0913d));
            mainWindow.TogglePrayerPanelCommand.Execute(null);
            SetPrivateField(mainWindow, "_prayerCountdownText", "MAGHRIB 00:00:00");
            RaisePropertyChanged(mainWindow, "PrayerCountdownText");
            AssertClose(626d, mainWindow.Width, "expanded main-window width");
            ApplyTheme(light: true, acrylic: true);
            RenderWindowContent(
                mainWindow,
                Path.Combine(artifactDirectory, "MainWindow-expanded-light-acrylic.png"));
            AssertCountdownFits(mainWindow);
            if (CanRunInteractiveNativeTests())
            {
                TestNativeAcrylicAndTintRestoration(mainWindow);
                NativeAcrylicVisualTests.RunPresentationHookChecks();
                if (nativeVisualRequested)
                {
                    NativeAcrylicVisualTests.Run(mainWindow, artifactDirectory);
                }
            }

            settingsWindow = new SettingsWindow(coordinator);
            PopulateSettingsWindow(settingsWindow);
            ApplyTheme(light: false, acrylic: false);
            RenderWindowContent(
                settingsWindow,
                Path.Combine(artifactDirectory, "SettingsWindow-dark.png"));
            AssertSettingsGeometry(settingsWindow);

            ApplyTheme(light: true, acrylic: true);
            RenderWindowContent(
                settingsWindow,
                Path.Combine(artifactDirectory, "SettingsWindow-light-acrylic.png"));
            AssertSettingsGeometry(settingsWindow);
            SettingsAppearanceTests.Run(coordinator, artifactDirectory);
            TooltipAndDetailsLayoutTests.Run(mainWindow, artifactDirectory);
            EventDetailsLifecycleTests.Run(mainWindow);
            TestPopupReopensAfterClose();

            Console.WriteLine("WeekCalendarTray UI verification passed.");
            Console.WriteLine($"Screenshots: {artifactDirectory}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            SafeClose(settingsWindow);
            SafeClose(mainWindow);
            ThemeManager.Shutdown();
            AppPaths.TestDataDirectory = null;
            application.Shutdown();
            TryDeleteDirectory(testDataDirectory);
        }
    }

    private static void TestCalendarDayColorsAndToggles()
    {
        var events = Enumerable.Range(0, 80)
            .Select(index => CreateEvent(
                $"calendar-{7 - (index % 8):00}",
                $"Long synthetic event {index:00} {new string('W', 100)}",
                SampleDate,
                index % 24))
            .ToArray();
        var day = new CalendarDay(SampleDate, 9, 37, true, true, true);
        var viewModel = new CalendarDayViewModel(day);

        viewModel.UpdateEvents(events, showEventIndicators: true, showEventPreviewOnHover: true);

        Assert(viewModel.EventIndicatorVisibility == Visibility.Visible, "enabled event stripe was not visible");
        Assert(viewModel.EventIndicatorBrushes.Count == 8, "repeated events must yield one segment per source");

        var expectedSources = events
            .Select(calendarEvent => calendarEvent.SourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(sourceId => sourceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < expectedSources.Length; index++)
        {
            var matchingEvent = events.First(calendarEvent =>
                string.Equals(calendarEvent.SourceId, expectedSources[index], StringComparison.OrdinalIgnoreCase));
            var agendaBrush = new CalendarEventViewModel(matchingEvent).AccentBrush;
            Assert(
                ReferenceEquals(viewModel.EventIndicatorBrushes[index], agendaBrush),
                $"day and agenda brushes differ for source {expectedSources[index]}");
        }

        Assert(
            viewModel.EventPreviewText?.Split(Environment.NewLine).Length == 7,
            "hover preview must retain six events plus its overflow line");

        viewModel.UpdateEvents(events, showEventIndicators: false, showEventPreviewOnHover: true);
        Assert(viewModel.EventIndicatorVisibility == Visibility.Collapsed, "event stripe toggle did not hide the stripe");
        Assert(viewModel.EventIndicatorBrushes.Count == 8, "hiding the stripe must not discard its source colors");

        viewModel.UpdateEvents(events, showEventIndicators: true, showEventPreviewOnHover: false);
        Assert(viewModel.EventPreviewText is null, "hover-preview toggle did not suppress preview text");
    }

    private static void TestPopupReopensAfterClose()
    {
        using var controller = new TrayApplicationController();

        ThemeManager.SetAcrylicEnabled(true);
        for (var index = 0; index < 20; index++)
        {
            InvokePrivate(controller, "ShowPopup", false);
            var popup = GetPrivateField<MainWindow>(controller, "_popup")
                ?? throw new InvalidOperationException($"ShowPopup did not create popup {index + 1}.");
            popup.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert(popup.IsVisible, $"popup reopen {index + 1} was not visible");
            var handle = new WindowInteropHelper(popup).Handle;
            Assert(handle != IntPtr.Zero, $"popup reopen {index + 1} had no HWND");
            Assert(HwndSource.FromHwnd(handle) is not null, $"popup reopen {index + 1} had no HwndSource");
            if (CanQueryNativeAcrylic())
            {
                Assert(
                    TryGetBackdrop(handle, out var backdrop) && backdrop == 3,
                    $"popup reopen {index + 1} did not have native transient-window backdrop state");
            }
            popup.Hide();
        }

        InvokePrivate(controller, "ShowPopup", false);
        var firstPopup = GetPrivateField<MainWindow>(controller, "_popup")
            ?? throw new InvalidOperationException("ShowPopup did not create its first popup.");
        Assert(firstPopup.IsVisible, "first popup was not visible");

        firstPopup.Close();
        Assert(
            GetPrivateField<MainWindow>(controller, "_popup") is null,
            "closing the first popup did not clear the controller reference");

        InvokePrivate(controller, "ShowPopup", false);
        var secondPopup = GetPrivateField<MainWindow>(controller, "_popup")
            ?? throw new InvalidOperationException("ShowPopup did not recreate its popup.");
        Assert(!ReferenceEquals(firstPopup, secondPopup), "ShowPopup reused the closed popup");
        Assert(secondPopup.IsVisible, "replacement popup was not visible");
    }

    private static void TestMeasuredDayStripe(MainWindow mainWindow)
    {
        var events = Enumerable.Range(0, 32)
            .Select(index => CreateEvent(
                $"source-{index % 8:00}",
                $"Measured event {index:00}",
                SampleDate,
                index % 24))
            .ToArray();
        var day = new CalendarDay(SampleDate, 9, 37, true, true, true);
        var viewModel = new CalendarDayViewModel(day);
        viewModel.UpdateEvents(events, showEventIndicators: true, showEventPreviewOnHover: true);

        var button = new Button
        {
            Content = viewModel.DayNumber,
            DataContext = viewModel,
            Style = (Style)mainWindow.Resources["DayButtonStyle"]
        };
        var host = new Grid
        {
            Width = 42d,
            Height = 42d
        };
        host.Children.Add(button);
        MeasureAndArrange(host, 42d, 42d);
        button.ApplyTemplate();
        host.UpdateLayout();

        var stripe = Descendants<Border>(button).Single(border =>
            Math.Abs(border.Width - 12d) < GeometryTolerance
            && Math.Abs(border.Height - 3d) < GeometryTolerance
            && border.Child is ItemsControl);
        var stripeItems = (ItemsControl)stripe.Child;
        stripeItems.UpdateLayout();

        AssertClose(12d, stripe.ActualWidth, "event stripe width");
        AssertClose(3d, stripe.ActualHeight, "event stripe height");
        Assert(stripeItems.Items.Count == 8, "measured stripe did not contain eight source segments");

        var expectedSegmentWidth = stripe.ActualWidth / stripeItems.Items.Count;
        for (var index = 0; index < stripeItems.Items.Count; index++)
        {
            var segment = stripeItems.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement
                ?? throw new InvalidOperationException($"Stripe segment {index} was not realized.");
            var bounds = BoundsRelativeTo(segment, stripe);

            AssertClose(expectedSegmentWidth, bounds.Width, $"stripe segment {index} width");
            AssertClose(index * expectedSegmentWidth, bounds.Left, $"stripe segment {index} position");
            Assert(
                bounds.Left >= -GeometryTolerance
                && bounds.Right <= stripe.ActualWidth + GeometryTolerance
                && bounds.Top >= -GeometryTolerance
                && bounds.Bottom <= stripe.ActualHeight + GeometryTolerance,
                $"stripe segment {index} overflowed the fixed stripe bounds");
        }

        var stripeBounds = BoundsRelativeTo(stripe, button);
        Assert(
            stripeBounds.Left >= -GeometryTolerance
            && stripeBounds.Right <= button.ActualWidth + GeometryTolerance
            && stripeBounds.Bottom <= button.ActualHeight + GeometryTolerance,
            "event stripe overflowed the day button");

        var expectedDayText = viewModel.DayNumber.ToString();
        var dayNumber = Descendants<FrameworkElement>(button).FirstOrDefault(element =>
            element is TextBlock textBlock && textBlock.Text == expectedDayText
            || element is AccessText accessText && accessText.Text == expectedDayText)
            ?? throw new InvalidOperationException("The rendered day-number text was not found.");
        var dayNumberBounds = BoundsRelativeTo(dayNumber, button);
        var overlap = Rect.Intersect(dayNumberBounds, stripeBounds);
        Assert(
            overlap.IsEmpty || overlap.Height <= GeometryTolerance,
            $"day number {dayNumberBounds} overlaps event stripe {stripeBounds} by {overlap.Height:0.##} px");
        AssertClose(2d, button.BorderThickness.Left, "selected-day border thickness");
    }

    private static void PopulateMainWindow(MainWindow mainWindow)
    {
        var sourceIds = Enumerable.Range(0, 8).Select(index => $"synthetic-{index:00}").ToArray();
        var dayIndex = 0;
        foreach (var day in mainWindow.Weeks.SelectMany(week => week.Days))
        {
            var events = Enumerable.Range(0, 1 + (dayIndex % sourceIds.Length))
                .Select(index => CreateEvent(
                    sourceIds[index],
                    $"Calendar item {dayIndex:00}-{index:00}",
                    day.Date,
                    (8 + index) % 24))
                .ToArray();
            day.UpdateEvents(events, showEventIndicators: true, showEventPreviewOnHover: true);
            dayIndex++;
        }

        mainWindow.SelectedDayEvents.Clear();
        for (var index = 0; index < 8; index++)
        {
            mainWindow.SelectedDayEvents.Add(new CalendarEventViewModel(CreateEvent(
                sourceIds[index],
                $"Synthetic agenda event {index + 1}: {new string('L', 80)}",
                SampleDate,
                8 + index)));
        }

        SetPrivateField(mainWindow, "_syncStatusText", "Synthetic calendars loaded; no personal data was read.");
        RaisePropertyChanged(mainWindow, "AgendaEmptyText");
        RaisePropertyChanged(mainWindow, "AgendaEmptyVisibility");
        RaisePropertyChanged(mainWindow, "SyncStatusText");
    }

    private static void PopulateSettingsWindow(SettingsWindow window)
    {
        window.SubscriptionsListBox.ItemsSource = Enumerable.Range(0, 5)
            .Select(index => new IcalSubscription
            {
                Id = $"synthetic-subscription-{index:00}",
                Name = $"Synthetic calendar {index + 1} with a very long source label {new string('N', 45)}",
                Url = $"https://example.invalid/calendar-{index}.ics",
                IsEnabled = index != 3
            })
            .ToArray();
        window.NameTextBox.Text = "Synthetic calendar with long input text";
        window.UrlTextBox.Text = "https://example.invalid/a/very/long/synthetic/calendar/path/feed.ics";
        window.ShowEventIndicatorsCheckBox.IsChecked = true;
        window.ShowEventPreviewCheckBox.IsChecked = true;
        window.AcrylicEnabledCheckBox.IsChecked = true;
        window.PrayerTimesEnabledCheckBox.IsChecked = true;
        window.PrayerNotificationsEnabledCheckBox.IsChecked = true;
        window.PrayerLocationTextBox.Text =
            "Synthetic location with a deliberately long city and region name";
        window.PrayerLocationStatusText.Text =
            "Resolved synthetic location: 51.893600, 5.091300. This long status verifies wrapping without covering the footer actions.";
        window.StatusText.Text =
            "Five synthetic calendars are ready. This intentionally long status verifies that the footer remains readable and does not overlap.";
        window.StartWithWindowsCheckBox.IsChecked = false;

        SetPrivateField(window, "_settingsLoaded", true);
        InvokePrivate(window, "SetBusy", false);
    }

    private static void AssertSettingsGeometry(SettingsWindow window)
    {
        var root = (FrameworkElement)window.Content;
        AssertNoOverlap(window.PrayerLocationStatusText, window.StatusText, root, "location status", "settings status");
        AssertNoOverlap(window.StatusText, window.SaveButton, root, "settings status", "Save button");
        AssertNoOverlap(window.StatusText, window.SyncNowButton, root, "settings status", "Sync button");
        AssertNoOverlap(window.SaveButton, window.SyncNowButton, root, "Save button", "Sync button");
    }

    private static void AssertCountdownFits(MainWindow window)
    {
        const string expectedText = "MAGHRIB 00:00:00";
        var countdown = Descendants<TextBlock>((FrameworkElement)window.Content)
            .Single(textBlock => textBlock.Text == expectedText);
        var typeface = new Media.Typeface(
            countdown.FontFamily,
            countdown.FontStyle,
            countdown.FontWeight,
            countdown.FontStretch);
        var formatted = new Media.FormattedText(
            expectedText,
            CultureInfo.CurrentCulture,
            countdown.FlowDirection,
            typeface,
            countdown.FontSize,
            countdown.Foreground,
            Media.VisualTreeHelper.GetDpi(countdown).PixelsPerDip);

        Assert(
            formatted.WidthIncludingTrailingWhitespace <= countdown.ActualWidth + GeometryTolerance,
            $"prayer countdown requires {formatted.WidthIncludingTrailingWhitespace:0.##} px but only {countdown.ActualWidth:0.##} px is available");
    }

    private static void TestNativeAcrylicAndTintRestoration(MainWindow window)
    {
        ThemeManager.SetAcrylicEnabled(true);
        ThemeManager.ApplyCurrentTheme();
        ThemeManager.ApplyCurrentTheme();
        window.Show();
        window.UpdateLayout();

        var handle = new WindowInteropHelper(window).Handle;
        var backdrop = 0;
        var requiresNativeBackdrop = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)
            && !SystemParameters.HighContrast
            && TryIsDwmCompositionEnabled()
            && TryGetBackdrop(handle, out backdrop);
        if (requiresNativeBackdrop)
        {
            Assert(backdrop == 3, $"expected transient-window backdrop 3, actual {backdrop}");
            Assert(window.Resources.Contains("WindowSurfaceBrush"), "enabled acrylic did not install its window-local surface tint");
            AssertEffectiveBrushOpaque(window, "WindowPanelBrush", "enabled acrylic menu");
        }
        else
        {
            AssertEffectiveBrushOpaque(window, "WindowSurfaceBrush", "acrylic fallback surface");
            AssertEffectiveBrushOpaque(window, "WindowPanelBrush", "acrylic fallback menu");
        }

        ThemeManager.SetAcrylicEnabled(false);
        ThemeManager.ApplyCurrentTheme();
        ThemeManager.ApplyCurrentTheme();

        Assert(
            !window.Resources.Contains("WindowSurfaceBrush"),
            "disabling acrylic did not remove the window-local surface tint");
        AssertEffectiveBrushOpaque(window, "WindowSurfaceBrush", "disabled acrylic surface");
        AssertEffectiveBrushOpaque(window, "WindowPanelBrush", "disabled acrylic menu");
        window.Hide();
    }

    private static bool TryIsDwmCompositionEnabled()
    {
        try
        {
            return DwmIsCompositionEnabled(out var enabled) >= 0 && enabled;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool TryGetBackdrop(IntPtr handle, out int backdrop)
    {
        try
        {
            return DwmGetWindowAttribute(handle, 38, out backdrop, Marshal.SizeOf<int>()) >= 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            backdrop = 0;
            return false;
        }
    }

    private static void AssertEffectiveBrushOpaque(Window window, string resourceName, string state)
    {
        var brush = window.FindResource(resourceName) as Media.SolidColorBrush
            ?? throw new InvalidOperationException($"{state} has no solid {resourceName}");
        Assert(brush.Color.A == byte.MaxValue, $"{state} {resourceName} alpha was {brush.Color.A}");
    }

    internal static CalendarEvent CreateEvent(string sourceId, string title, DateOnly date, int hour)
    {
        var localTime = date.ToDateTime(new TimeOnly(hour, 0));
        var start = new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
        return new CalendarEvent(
            sourceId,
            $"Source {sourceId}",
            $"{sourceId}-{hour:00}-{title.GetHashCode(StringComparison.Ordinal):X8}",
            title,
            start,
            start.AddHours(1),
            false,
            "Synthetic location",
            "Synthetic description",
            "Synthetic organizer",
            "https://example.invalid/event");
    }

    private static void ApplyTheme(bool light, bool acrylic)
    {
        ThemeManager.SetAppearanceOptions(
            acrylic,
            ThemeManager.AcrylicOpacityPercent,
            light ? AppThemePreference.Light : AppThemePreference.Dark);
        Assert(ThemeManager.IsLightTheme == light, "forced theme preference did not update the active theme");
    }

    internal static void RenderWindowContent(Window window, string path)
    {
        var root = window.Content as FrameworkElement
            ?? throw new InvalidOperationException($"{window.GetType().Name} has no renderable content.");
        MeasureAndArrange(root, window.Width, window.Height);

        var pixelWidth = (int)Math.Ceiling(window.Width);
        var pixelHeight = (int)Math.Ceiling(window.Height);
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96d,
            96d,
            Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        AssertBitmapHasContent(bitmap, window.GetType().Name);

        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static void AssertBitmapHasContent(BitmapSource bitmap, string name)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        var visiblePixels = 0;
        var sampledColors = new HashSet<int>();
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] == 0)
            {
                continue;
            }

            visiblePixels++;
            if ((offset / 4) % 97 == 0)
            {
                sampledColors.Add(
                    (pixels[offset + 2] << 16)
                    | (pixels[offset + 1] << 8)
                    | pixels[offset]);
            }
        }

        Assert(visiblePixels > bitmap.PixelWidth * bitmap.PixelHeight / 4, $"{name} render was mostly transparent");
        Assert(sampledColors.Count >= 8, $"{name} render did not contain enough visual variation");
    }

    internal static void MeasureAndArrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0d, 0d, width, height));
        element.UpdateLayout();
    }

    internal static Rect BoundsRelativeTo(FrameworkElement element, Media.Visual ancestor)
    {
        return element.TransformToAncestor(ancestor)
            .TransformBounds(new Rect(new Point(), element.RenderSize));
    }

    internal static void AssertNoOverlap(
        FrameworkElement first,
        FrameworkElement second,
        Media.Visual ancestor,
        string firstName,
        string secondName)
    {
        var firstBounds = BoundsRelativeTo(first, ancestor);
        var secondBounds = BoundsRelativeTo(second, ancestor);
        Assert(
            !firstBounds.IntersectsWith(secondBounds),
            $"{firstName} {firstBounds} overlaps {secondName} {secondBounds}");
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    internal static void SetPrivateField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }

    internal static T? GetPrivateField<T>(object target, string name)
        where T : class
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, name);
        return field.GetValue(target) as T;
    }

    private static void RaisePropertyChanged(object target, string propertyName)
    {
        InvokePrivate(target, "OnPropertyChanged", propertyName);
    }

    internal static void InvokePrivate(object target, string name, params object?[] arguments)
    {
        var method = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(candidate =>
            {
                if (candidate.Name != name)
                {
                    return false;
                }

                var parameters = candidate.GetParameters();
                return parameters.Length == arguments.Length
                    && parameters.Zip(arguments).All(pair =>
                        pair.Second is null
                        || pair.First.ParameterType.IsInstanceOfType(pair.Second));
            })
            ?? throw new MissingMethodException(target.GetType().FullName, name);
        method.Invoke(target, arguments);
    }

    private static string GetArtifactDirectory(IReadOnlyList<string> args)
    {
        const string prefix = "--artifacts=";
        var configured = args.FirstOrDefault(argument =>
            argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return configured is null
            ? Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "ui"))
            : Path.GetFullPath(configured[prefix.Length..]);
    }

    private static void AssertClose(double expected, double actual, string subject)
    {
        Assert(Math.Abs(expected - actual) <= GeometryTolerance, $"{subject}: expected {expected}, actual {actual}");
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void SafeClose(Window? window)
    {
        try
        {
            window?.Close();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool CanRunInteractiveNativeTests()
    {
        return Environment.UserInteractive && GetSystemMetrics(0x1000) == 0;
    }

    private static void WaitForThemeInitialization()
    {
        var property = typeof(ThemeManager).GetProperty(
            "InitializationReady",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (property?.GetValue(null) is Task initialization)
        {
            if (!initialization.IsCompleted)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                _ = initialization.ContinueWith(
                    _ => WpfApplication.Current.Dispatcher.BeginInvoke(
                        (Action)(() => frame.Continue = false)),
                    TaskScheduler.Default);
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }

            initialization.GetAwaiter().GetResult();
        }
    }

    private static bool CanQueryNativeAcrylic()
    {
        return CanRunInteractiveNativeTests()
            && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)
            && !SystemParameters.HighContrast
            && TryIsDwmCompositionEnabled();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        out int attributeValue,
        int attributeSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
