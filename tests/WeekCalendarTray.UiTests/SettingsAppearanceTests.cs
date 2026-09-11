using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WeekCalendarTray.Core;
using Media = System.Windows.Media;

namespace WeekCalendarTray.UiTests;

internal static class SettingsAppearanceTests
{
    private const int PreviewOpacityPercent = 20;

    public static void Run(CalendarSyncCoordinator coordinator, string artifactDirectory)
    {
        var incomingAppearance = AppearanceState.Capture();
        SettingsWindow? window = null;

        try
        {
            window = new SettingsWindow(coordinator);
            window.Show();
            WaitUntil(
                () => window.SaveButton.IsEnabled,
                "Settings did not finish loading from the isolated test store");

            var loadedAppearance = AppearanceState.Capture();
            AssertThemePreview(window, AppThemePreference.System);
            AssertThemePreview(window, AppThemePreference.Light);
            AssertThemePreview(window, AppThemePreference.Dark);

            window.AcrylicEnabledCheckBox.IsChecked = true;
            window.AcrylicOpacitySlider.Value = PreviewOpacityPercent;
            Program.Assert(ThemeManager.IsAcrylicEnabled, "Acrylic checkbox did not apply its live preview");
            Program.Assert(
                ThemeManager.AcrylicOpacityPercent == PreviewOpacityPercent,
                "Acrylic opacity slider did not apply its live preview");
            Program.Assert(
                window.AcrylicOpacityValueText.Text == $"{PreviewOpacityPercent}%",
                "Acrylic opacity value text did not track the slider");
            Program.Assert(
                window.AcrylicOpacityPanel.IsEnabled,
                "Acrylic opacity controls were not enabled with Acrylic selected");

            RenderThemeDropDown(window, artifactDirectory);
            RenderPrayerSection(window, AppThemePreference.Dark, artifactDirectory);
            RenderPrayerSection(window, AppThemePreference.Light, artifactDirectory);

            var closeButton = Program.Descendants<Button>((FrameworkElement)window.Content)
                .Single(button => string.Equals(button.Content as string, "Close", StringComparison.Ordinal));
            closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Program.Assert(!window.IsVisible, "Settings Close button did not close the window");
            loadedAppearance.AssertCurrent("Settings Close did not restore the loaded appearance");
            window = null;
        }
        finally
        {
            if (window is not null)
            {
                try
                {
                    window.Close();
                }
                catch (InvalidOperationException)
                {
                }
            }

            incomingAppearance.Apply();
        }
    }

    private static void AssertThemePreview(SettingsWindow window, AppThemePreference preference)
    {
        window.ThemePreferenceComboBox.SelectedIndex = (int)preference;
        Program.Assert(
            ThemeManager.ThemePreference == preference,
            $"{preference} selection did not apply its live preview");

        if (preference != AppThemePreference.System)
        {
            Program.Assert(
                ThemeManager.IsLightTheme == (preference == AppThemePreference.Light),
                $"{preference} selection produced the wrong active palette");
        }
    }

    private static void RenderThemeDropDown(SettingsWindow window, string artifactDirectory)
    {
        var body = FindBodyScrollViewer(window);
        body.ScrollToTop();
        window.ThemePreferenceComboBox.SelectedIndex = (int)AppThemePreference.Dark;
        window.ThemePreferenceComboBox.ApplyTemplate();
        window.ThemePreferenceComboBox.IsDropDownOpen = true;

        var popup = window.ThemePreferenceComboBox.Template.FindName(
            "PART_Popup",
            window.ThemePreferenceComboBox) as Popup;
        Program.Assert(popup is not null, "Theme selector template did not expose its popup");
        WaitUntil(
            () => popup!.IsOpen
                && popup.Child is FrameworkElement { ActualWidth: > 0d, ActualHeight: > 0d },
            "Theme selector popup did not open");

        RenderElement(
            (FrameworkElement)popup!.Child,
            Path.Combine(artifactDirectory, "SettingsWindow-theme-dropdown-dark.png"));
        window.ThemePreferenceComboBox.IsDropDownOpen = false;
    }

    private static void RenderPrayerSection(
        SettingsWindow window,
        AppThemePreference preference,
        string artifactDirectory)
    {
        window.ThemePreferenceComboBox.SelectedIndex = (int)preference;
        var body = FindBodyScrollViewer(window);
        body.ScrollToBottom();
        FlushLayout(window);

        var prayerBounds = Program.BoundsRelativeTo(window.PrayerLocationTextBox, body);
        var viewport = new Rect(0d, 0d, body.ActualWidth, body.ActualHeight);
        Program.Assert(
            viewport.IntersectsWith(prayerBounds),
            $"Prayer controls were not visible with Settings scrolled to the bottom in {preference} mode");

        var suffix = preference == AppThemePreference.Dark ? "dark" : "light";
        Program.RenderWindowContent(
            window,
            Path.Combine(artifactDirectory, $"SettingsWindow-prayer-{suffix}.png"));
    }

    private static ScrollViewer FindBodyScrollViewer(SettingsWindow window)
    {
        return Program.Descendants<ScrollViewer>((FrameworkElement)window.Content)
            .Where(scrollViewer => scrollViewer.IsAncestorOf(window.PrayerLocationTextBox))
            .OrderByDescending(scrollViewer => scrollViewer.ActualHeight)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Settings body ScrollViewer was not found.");
    }

    private static void RenderElement(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96d, 96d, Media.PixelFormats.Pbgra32);
        bitmap.Render(element);
        AssertRenderedContent(bitmap, "theme selector popup");

        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static void AssertRenderedContent(BitmapSource bitmap, string subject)
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
            if ((offset / 4) % 17 == 0)
            {
                sampledColors.Add(
                    (pixels[offset + 2] << 16)
                    | (pixels[offset + 1] << 8)
                    | pixels[offset]);
            }
        }

        Program.Assert(visiblePixels > 0, $"{subject} render was transparent");
        Program.Assert(sampledColors.Count >= 2, $"{subject} render did not contain visible content");
    }

    private static void FlushLayout(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
    }

    private static void WaitUntil(Func<bool> condition, string failureMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException(failureMessage);
            }

            var frame = new DispatcherFrame();
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                (Action)(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }

    private readonly record struct AppearanceState(
        bool AcrylicEnabled,
        int AcrylicOpacityPercent,
        AppThemePreference ThemePreference)
    {
        public static AppearanceState Capture()
        {
            return new AppearanceState(
                ThemeManager.IsAcrylicEnabled,
                ThemeManager.AcrylicOpacityPercent,
                ThemeManager.ThemePreference);
        }

        public void Apply()
        {
            ThemeManager.SetAppearanceOptions(
                AcrylicEnabled,
                AcrylicOpacityPercent,
                ThemePreference);
        }

        public void AssertCurrent(string message)
        {
            Program.Assert(
                ThemeManager.IsAcrylicEnabled == AcrylicEnabled
                && ThemeManager.AcrylicOpacityPercent == AcrylicOpacityPercent
                && ThemeManager.ThemePreference == ThemePreference,
                message);
        }
    }
}
