using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WeekCalendarTray.Core;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;
using Media = System.Windows.Media;

namespace WeekCalendarTray.UiTests;

internal static class NativeAcrylicVisualTests
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmRoundPreference = 2;
    private const int DwmTransientWindowBackdrop = 3;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const int ComparisonInset = 24;

    public static void RunPresentationHookChecks()
    {
        if (!CanVerifyNativeBackdrop()) return;
        var enabled = ThemeManager.IsAcrylicEnabled;
        var opacity = ThemeManager.AcrylicOpacityPercent;
        var theme = ThemeManager.ThemePreference;
        var owner = new Window { Width = 120, Height = 120, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        var child = new Window { Width = 120, Height = 120, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        var external = new Window { Width = 120, Height = 120, ShowInTaskbar = false };
        try
        {
            ThemeManager.SetAcrylicEnabled(true);
            owner.Show();
            child.Owner = owner;
            child.Show();
            owner.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var ownerHandle = new WindowInteropHelper(owner).Handle;
            var childHandle = new WindowInteropHelper(child).Handle;
            var externalHandle = new WindowInteropHelper(external).EnsureHandle();
            var registration = GetAcrylicRegistration(owner);
            AssertPresentationMessagePolicy(registration, ownerHandle, childHandle, externalHandle, child, false);
            AssertSharedWindowSurfaceTint(owner, child);
            owner.Hide();
            AssertMessageIgnored(registration, ownerHandle, 0x0086, IntPtr.Zero, childHandle, "hidden owner");
            ThemeManager.SetAcrylicEnabled(false);
            AssertAcrylicRegistrationDisposed(owner, registration);
            owner.Show();
            child.Show();
            ThemeManager.SetAcrylicEnabled(true);
            var childRegistration = GetAcrylicRegistration(child);
            child.Close();
            AssertAcrylicRegistrationDisposed(child, childRegistration);
            Console.WriteLine("Acrylic presentation message policy and hook cleanup passed.");
        }
        finally
        {
            external.Close();
            child.Close();
            owner.Close();
            ThemeManager.SetAppearanceOptions(enabled, opacity, theme);
        }
    }

    public static void RunFirstOpen(string artifactDirectory)
    {
        if (!CanVerifyNativeBackdrop()) return;
        var previousEnabled = ThemeManager.IsAcrylicEnabled;
        var previousOpacity = ThemeManager.AcrylicOpacityPercent;
        var previousTheme = ThemeManager.ThemePreference;
        var backdrop = new SyntheticBackdropWindow(SystemParameters.WorkArea);
        try
        {
            backdrop.UsePaletteA();
            backdrop.Show();
            FlushDesktop(backdrop);
            if (!CanCaptureSyntheticBackdrop(backdrop, out var status))
            {
                Console.WriteLine($"First-open desktop pixels skipped: {status}");
                return;
            }

            foreach (var opacity in new[] { 20, 75 })
            {
                ThemeManager.SetAppearanceOptions(true, opacity, AppThemePreference.Dark);
                using var controller = new TrayApplicationController();
                for (var opening = 0; opening < 3; opening++)
                {
                    InvokePrivate(controller, "ShowPopup", false);
                    var popup = GetPrivateField<MainWindow>(controller, "_popup")!;
                    SetPrivateField(popup, "_keepOpenForChildWindow", true);
                    FlushDesktop(popup);
                    var registration = GetAcrylicRegistration(popup);
                    var deadline = DateTime.UtcNow.AddSeconds(2);
                    while (!GetPrivateFieldValue<bool>(registration, "_firstFrameRefreshCompleted")
                        && DateTime.UtcNow < deadline)
                        FlushDesktop(popup);
                    Assert(GetPrivateFieldValue<bool>(registration, "_firstFrameRefreshCompleted"),
                        "first-frame refresh did not finish before the first-open capture");
                    using var pair = CaptureResponsivePair(popup, backdrop, artifactDirectory,
                        $"MainWindow-first-open-{opacity}-{opening}");
                    Console.WriteLine($"First-open acrylic response at {opacity}% (open {opening + 1}): {pair.Difference:0.##}");
                    Assert(pair.Difference >= 2d, "first-open calendar stayed opaque before any panel interaction");
                    popup.Hide();
                }
            }
        }
        finally
        {
            backdrop.Close();
            ThemeManager.SetAppearanceOptions(previousEnabled, previousOpacity, previousTheme);
        }
    }

    public static void Run(MainWindow window, string artifactDirectory)
    {
        if (!CanVerifyNativeBackdrop())
        {
            Console.WriteLine("Native desktop acrylic pixels skipped: DWM composition is unavailable.");
            return;
        }

        SetPrivateField(window, "_keepOpenForChildWindow", true);
        SetPrivateField(window, "_prayerTimesEnabled", false);
        InvokePrivate(window, "UpdatePrayerLayout", false);

        var workArea = SystemParameters.WorkArea;
        var backdrop = new SyntheticBackdropWindow(workArea);
        EventDetailsWindow? detailsWindow = null;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        const double detailsGap = 12d;
        const double detailsWidth = 460d;
        var combinedWidth = window.Width + detailsGap + detailsWidth;
        window.Left = workArea.Left + Math.Max(0d, (workArea.Width - combinedWidth) / 2d);
        window.Top = workArea.Top + Math.Max(0d, (workArea.Height - window.Height) / 2d);
        window.Topmost = true;

        try
        {
            backdrop.UsePaletteA();
            backdrop.Show();
            FlushDesktop(backdrop);
            if (!CanCaptureSyntheticBackdrop(backdrop, out var captureStatus))
            {
                Console.WriteLine(
                    $"Native desktop acrylic pixels skipped: screen capture cannot see the synthetic test backdrop ({captureStatus}).");
                return;
            }

            ThemeManager.SetAcrylicEnabled(true);
            ThemeManager.ApplyCurrentTheme();
            window.Show();
            window.Activate();
            FlushDesktop(window);

            var handle = new WindowInteropHelper(window).Handle;
            var ownerRegistration = GetAcrylicRegistration(window);
            AssertDwmAttribute(handle, DwmwaSystemBackdropType, DwmTransientWindowBackdrop, "system backdrop");
            AssertOptionalDwmAttribute(handle, DwmwaWindowCornerPreference, DwmRoundPreference, "corner preference");
            AssertOptionalDwmAttribute(
                handle,
                DwmwaBorderColor,
                unchecked((int)DwmColorNone),
                "border color");

            backdrop.UseWhite();
            FlushDesktop(backdrop);
            using var white = CaptureWindow(window, Path.Combine(
                artifactDirectory,
                "MainWindow-native-acrylic-white.png"));
            backdrop.UseDark();
            FlushDesktop(backdrop);
            using var dark = CaptureWindow(window, Path.Combine(
                artifactDirectory,
                "MainWindow-native-acrylic-dark.png"));
            backdrop.UsePattern();
            FlushDesktop(backdrop);
            using var pattern = CaptureWindow(window, Path.Combine(
                artifactDirectory,
                "MainWindow-native-acrylic-pattern.png"));

            using var acrylicPair = CaptureResponsivePair(
                window,
                backdrop,
                artifactDirectory,
                "MainWindow-native-acrylic-backdrop");

            // Event details are now inline. Keep a synthetic owned window here to
            // exercise the native backdrop policy still used by Settings and Add.
            detailsWindow = new EventDetailsWindow(CreateSyntheticDetailsEvent()) { Owner = window };
            detailsWindow.Show();
            detailsWindow.Activate();
            FlushDesktop(detailsWindow);

            AssertOwnedChildActivation(window, detailsWindow, workArea);
            AssertSharedWindowSurfaceTint(window, detailsWindow);
            var detailsHandle = new WindowInteropHelper(detailsWindow).Handle;
            var detailsRegistration = GetAcrylicRegistration(detailsWindow);
            AssertDwmAttribute(handle, DwmwaSystemBackdropType, DwmTransientWindowBackdrop,
                "inactive owner system backdrop");
            AssertDwmAttribute(detailsHandle, DwmwaSystemBackdropType, DwmTransientWindowBackdrop,
                "active details system backdrop");
            AssertPresentationMessagePolicy(
                ownerRegistration,
                handle,
                detailsHandle,
                new WindowInteropHelper(backdrop).Handle,
                detailsWindow);

            using var inactiveOwnerPair = CaptureResponsivePair(
                window,
                backdrop,
                artifactDirectory,
                "MainWindow-native-owner-inactive-details-active-backdrop");
            Assert(
                inactiveOwnerPair.Difference >= 2d,
                $"inactive owner stopped responding to its backdrop while details was active ({inactiveOwnerPair.Difference:0.##})");
            Assert(
                inactiveOwnerPair.Difference >= acrylicPair.Difference * 0.5d,
                $"inactive owner backdrop response {inactiveOwnerPair.Difference:0.##} fell below half its active response {acrylicPair.Difference:0.##}");

            detailsWindow.Close();
            AssertAcrylicRegistrationDisposed(detailsWindow, detailsRegistration);
            detailsWindow = null;
            window.Activate();
            FlushDesktop(window);

            ThemeManager.SetAcrylicEnabled(false);
            ThemeManager.ApplyCurrentTheme();
            FlushDesktop(window);
            AssertAcrylicRegistrationDisposed(window, ownerRegistration);

            backdrop.UsePaletteA();
            FlushDesktop(backdrop);
            using var solidA = CaptureWindow(window, Path.Combine(
                artifactDirectory,
                "MainWindow-native-solid-backdrop-a.png"));
            backdrop.UsePaletteB();
            FlushDesktop(backdrop);
            using var solidB = CaptureWindow(window, Path.Combine(
                artifactDirectory,
                "MainWindow-native-solid-backdrop-b.png"));

            var acrylicDifference = acrylicPair.Difference;
            var solidDifference = MeanPixelDifference(solidA, solidB, ComparisonInset);
            Console.WriteLine(
                $"Native acrylic pixel response: enabled={acrylicDifference:0.##}, disabled={solidDifference:0.##}.");
            Assert(
                acrylicDifference >= 2d,
                $"desktop-composited acrylic did not respond visibly to the synthetic backdrop ({acrylicDifference:0.##})");
            Assert(
                solidDifference <= 0.75d,
                $"disabled acrylic still responded to the synthetic backdrop ({solidDifference:0.##})");
            Assert(
                acrylicDifference >= (solidDifference * 3d) + 1d,
                $"acrylic response {acrylicDifference:0.##} was not meaningfully above solid response {solidDifference:0.##}");
        }
        finally
        {
            detailsWindow?.Close();
            ThemeManager.SetAcrylicEnabled(false);
            ThemeManager.ApplyCurrentTheme();
            window.Hide();
            backdrop.Close();
        }
    }

    private static bool CanVerifyNativeBackdrop()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)
            || SystemParameters.HighContrast)
        {
            return false;
        }

        try
        {
            return DwmIsCompositionEnabled(out var enabled) >= 0 && enabled;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static Drawing.Bitmap CaptureWindow(Window window, string path)
    {
        var handle = new WindowInteropHelper(window).Handle;
        Assert(GetWindowRect(handle, out var bounds), "GetWindowRect failed for the shown MainWindow");

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        Assert(width > 0 && height > 0, $"invalid native window bounds {width}x{height}");

        var bitmap = new Drawing.Bitmap(width, height, Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                new Drawing.Size(width, height),
                Drawing.CopyPixelOperation.SourceCopy);
        }

        bitmap.Save(path, Imaging.ImageFormat.Png);
        return bitmap;
    }

    private static bool CanCaptureSyntheticBackdrop(Window backdrop, out string status)
    {
        var handle = new WindowInteropHelper(backdrop).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds))
        {
            status = "synthetic backdrop has no readable native bounds";
            return false;
        }

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width < 4 || height < 4)
        {
            status = $"synthetic backdrop bounds are invalid ({width}x{height})";
            return false;
        }

        try
        {
            var sampleY = bounds.Top + (height / 8);
            var left = CaptureScreenPixel(bounds.Left + (width / 4), sampleY);
            var right = CaptureScreenPixel(bounds.Left + ((width * 3) / 4), sampleY);
            status = $"left=#{left.R:X2}{left.G:X2}{left.B:X2}, right=#{right.R:X2}{right.G:X2}{right.B:X2}";
            return IsNear(left, Drawing.Color.FromArgb(0xD0, 0x20, 0x40))
                && IsNear(right, Drawing.Color.FromArgb(0x20, 0x40, 0xD0));
        }
        catch (Exception exception) when (exception is ExternalException
            or System.ComponentModel.Win32Exception
            or InvalidOperationException)
        {
            status = $"screen sampling failed: {exception.GetType().Name}";
            return false;
        }
    }

    private static Drawing.Color CaptureScreenPixel(int x, int y)
    {
        using var bitmap = new Drawing.Bitmap(1, 1, Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new Drawing.Size(1, 1), Drawing.CopyPixelOperation.SourceCopy);
        return bitmap.GetPixel(0, 0);
    }

    private static bool IsNear(Drawing.Color actual, Drawing.Color expected)
    {
        const int tolerance = 24;
        return Math.Abs(actual.R - expected.R) <= tolerance
            && Math.Abs(actual.G - expected.G) <= tolerance
            && Math.Abs(actual.B - expected.B) <= tolerance;
    }

    private static CapturePair CaptureResponsivePair(
        Window window,
        SyntheticBackdropWindow backdrop,
        string artifactDirectory,
        string fileStem)
    {
        Drawing.Bitmap? first = null;
        Drawing.Bitmap? second = null;
        double difference = 0d;

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            first?.Dispose();
            second?.Dispose();

            backdrop.UsePaletteA();
            FlushDesktop(backdrop);
            first = CaptureWindow(window, Path.Combine(
                artifactDirectory,
                $"{fileStem}-a.png"));
            backdrop.UsePaletteB();
            FlushDesktop(backdrop);
            second = CaptureWindow(window, Path.Combine(
                artifactDirectory,
                $"{fileStem}-b.png"));
            difference = MeanPixelDifference(first, second, ComparisonInset);
            if (difference >= 2d)
            {
                break;
            }

            Thread.Sleep(200);
        }

        return new CapturePair(first!, second!, difference);
    }

    private static CalendarEventViewModel CreateSyntheticDetailsEvent()
    {
        var date = new DateOnly(2026, 9, 10);
        var localTime = date.ToDateTime(new TimeOnly(10, 0));
        var start = new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
        return new CalendarEventViewModel(new CalendarEvent(
            "native-owner-child-test",
            "Synthetic test calendar",
            "native-owner-child-event",
            "Synthetic event details acrylic verification",
            start,
            start.AddHours(1),
            false,
            "Synthetic test location",
            "Synthetic content only",
            "Synthetic organizer",
            "https://example.invalid/native-owner-child-test"));
    }

    private static void AssertOwnedChildActivation(
        MainWindow owner,
        EventDetailsWindow child,
        Rect workArea)
    {
        var ownerHandle = new WindowInteropHelper(owner).Handle;
        var childHandle = new WindowInteropHelper(child).Handle;
        Assert(child.Owner == owner, "event details lost its MainWindow owner");
        Assert(child.IsActive, "owned event details is not the active WPF window");
        Assert(!owner.IsActive, "owner remained active while its event details child had focus");
        Assert(GetForegroundWindow() == childHandle,
            "owned event details did not hold the real foreground HWND");
        Assert(ownerHandle != IntPtr.Zero && childHandle != IntPtr.Zero,
            "owner or details window did not have a native HWND");

        var ownerBounds = new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight);
        var childBounds = new Rect(child.Left, child.Top, child.ActualWidth, child.ActualHeight);
        Assert(!ownerBounds.IntersectsWith(childBounds),
            $"owner {ownerBounds} overlaps active details {childBounds}");
        Assert(Contains(workArea, ownerBounds), $"owner {ownerBounds} is outside work area {workArea}");
        Assert(Contains(workArea, childBounds), $"active details {childBounds} is outside work area {workArea}");
    }

    private static void AssertSharedWindowSurfaceTint(Window owner, Window child)
    {
        Assert(owner.Resources.Contains("WindowSurfaceBrush"),
            "acrylic owner has no window-local surface tint");
        Assert(child.Resources.Contains("WindowSurfaceBrush"),
            "acrylic details has no window-local surface tint");
        var ownerBrush = owner.Resources["WindowSurfaceBrush"] as Media.SolidColorBrush
            ?? throw new InvalidOperationException("owner WindowSurfaceBrush is not a solid color");
        var childBrush = child.Resources["WindowSurfaceBrush"] as Media.SolidColorBrush
            ?? throw new InvalidOperationException("details WindowSurfaceBrush is not a solid color");
        var expectedAlpha = (byte)Math.Round(ThemeManager.AcrylicOpacityPercent * 2.55d);
        Assert(ownerBrush.Color.A == expectedAlpha,
            $"owner surface alpha was {ownerBrush.Color.A}, expected {expectedAlpha}");
        Assert(childBrush.Color.A == expectedAlpha,
            $"details surface alpha was {childBrush.Color.A}, expected {expectedAlpha}");
        Assert(ownerBrush.Color == childBrush.Color,
            $"owner surface {ownerBrush.Color} differs from details surface {childBrush.Color}");
    }

    private static object GetAcrylicRegistration(Window window)
    {
        var registrations = GetAcrylicRegistrations();
        return registrations[window]
            ?? throw new InvalidOperationException($"{window.GetType().Name} has no acrylic registration");
    }

    private static System.Collections.IDictionary GetAcrylicRegistrations()
    {
        var field = typeof(AcrylicWindowManager).GetField(
            "Registrations",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(AcrylicWindowManager).FullName, "Registrations");
        return field.GetValue(null) as System.Collections.IDictionary
            ?? throw new InvalidOperationException("acrylic registration table is unavailable");
    }

    private static void AssertPresentationMessagePolicy(
        object registration,
        IntPtr ownerHandle,
        IntPtr childHandle,
        IntPtr externalHandle,
        Window activeChild,
        bool requireActiveChild = true)
    {
        const int wmActivate = 0x0006;
        const int wmSetFocus = 0x0007;
        const int wmNcActivate = 0x0086;

        Assert(GetPrivateField<HwndSource>(registration, "_windowSource") is not null,
            "owner presentation hook was not attached");
        AssertMessageIgnored(registration, ownerHandle, wmActivate, IntPtr.Zero, IntPtr.Zero,
            "WM_ACTIVATE");
        AssertMessageIgnored(registration, ownerHandle, wmSetFocus, IntPtr.Zero, IntPtr.Zero,
            "WM_SETFOCUS");
        AssertMessageIgnored(registration, ownerHandle, wmNcActivate, new IntPtr(1), childHandle,
            "active WM_NCACTIVATE");
        AssertMessageIgnored(registration, ownerHandle, wmNcActivate, IntPtr.Zero, IntPtr.Zero,
            "inactive WM_NCACTIVATE with zero target");
        AssertMessageIgnored(registration, ownerHandle, wmNcActivate, IntPtr.Zero, new IntPtr(-1),
            "inactive WM_NCACTIVATE with sentinel target");
        AssertMessageIgnored(registration, ownerHandle, wmNcActivate, IntPtr.Zero, externalHandle,
            "inactive WM_NCACTIVATE with external target");

        var foregroundBefore = GetForegroundWindow();
        var childWasActive = activeChild.IsActive;
        if (requireActiveChild)
            Assert(foregroundBefore == childHandle && childWasActive,
                "details child did not own focus before inactive presentation test");
        var (result, handled) = InvokeWindowProc(
            registration,
            ownerHandle,
            wmNcActivate,
            IntPtr.Zero,
            childHandle);
        var testedOwner = GetPrivateField<Window>(registration, "_window");
        Assert(handled, $"inactive WM_NCACTIVATE was not handled: visible={testedOwner?.IsVisible}, style={testedOwner?.WindowStyle}, state={testedOwner?.WindowState}, applied={GetPrivateFieldValue<bool>(registration, "_backdropApplied")}, disposed={GetPrivateFieldValue<bool>(registration, "_disposed")}, owner={ownerHandle}, child={childHandle}");
        Assert(result == new IntPtr(1), $"inactive WM_NCACTIVATE returned {result}, expected 1");
        Assert(GetForegroundWindow() == foregroundBefore && activeChild.IsActive == childWasActive,
            "presentation-only WM_NCACTIVATE handling changed input focus");
    }

    private static void AssertMessageIgnored(
        object registration,
        IntPtr handle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        string subject)
    {
        var foregroundBefore = GetForegroundWindow();
        var (result, handled) = InvokeWindowProc(registration, handle, message, wParam, lParam);
        Assert(!handled, $"presentation hook intercepted {subject}");
        Assert(result == IntPtr.Zero, $"presentation hook returned {result} for {subject}");
        Assert(GetForegroundWindow() == foregroundBefore, $"{subject} reflection test changed foreground focus");
    }

    private static (IntPtr Result, bool Handled) InvokeWindowProc(
        object registration,
        IntPtr handle,
        int message,
        IntPtr wParam,
        IntPtr lParam)
    {
        var method = registration.GetType().GetMethod(
            "WindowProc",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(registration.GetType().FullName, "WindowProc");
        object?[] arguments = [handle, message, wParam, lParam, false];
        var result = method.Invoke(registration, arguments) is IntPtr pointer
            ? pointer
            : throw new InvalidOperationException("WindowProc did not return an IntPtr");
        return (result, arguments[4] is true);
    }

    private static void AssertAcrylicRegistrationDisposed(Window window, object registration)
    {
        Assert(GetPrivateField<HwndSource>(registration, "_windowSource") is null,
            $"{window.GetType().Name} retained its presentation hook after cleanup");
        Assert(GetPrivateFieldValue<bool>(registration, "_disposed"),
            $"{window.GetType().Name} acrylic registration was not disposed");
        Assert(!GetAcrylicRegistrations().Contains(window),
            $"{window.GetType().Name} remained in the acrylic registration table after cleanup");
    }

    private static T? GetPrivateField<T>(object target, string name)
        where T : class
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, name);
        return field.GetValue(target) as T;
    }

    private static T GetPrivateFieldValue<T>(object target, string name)
        where T : struct
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, name);
        return field.GetValue(target) is T value
            ? value
            : throw new InvalidOperationException($"{target.GetType().Name}.{name} is not {typeof(T).Name}");
    }

    private static bool Contains(Rect outer, Rect inner)
    {
        const double tolerance = 1d;
        return inner.Left >= outer.Left - tolerance
            && inner.Top >= outer.Top - tolerance
            && inner.Right <= outer.Right + tolerance
            && inner.Bottom <= outer.Bottom + tolerance;
    }

    private static double MeanPixelDifference(
        Drawing.Bitmap first,
        Drawing.Bitmap second,
        int inset)
    {
        Assert(first.Size == second.Size, "native captures have different dimensions");
        Assert(
            first.Width > inset * 2 && first.Height > inset * 2,
            "native capture is too small for its comparison inset");

        long totalDifference = 0;
        long samples = 0;
        for (var y = inset; y < first.Height - inset; y += 3)
        {
            for (var x = inset; x < first.Width - inset; x += 3)
            {
                var firstPixel = first.GetPixel(x, y);
                var secondPixel = second.GetPixel(x, y);
                totalDifference += Math.Abs(firstPixel.R - secondPixel.R);
                totalDifference += Math.Abs(firstPixel.G - secondPixel.G);
                totalDifference += Math.Abs(firstPixel.B - secondPixel.B);
                samples += 3;
            }
        }

        return samples == 0 ? 0d : (double)totalDifference / samples;
    }

    private static void FlushDesktop(FrameworkElement element)
    {
        element.UpdateLayout();
        element.Dispatcher.Invoke(
            () => { },
            DispatcherPriority.ContextIdle);
        try
        {
            _ = DwmFlush();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        Thread.Sleep(120);
    }

    private static void AssertDwmAttribute(
        IntPtr handle,
        int attribute,
        int expected,
        string subject)
    {
        var result = DwmGetWindowAttribute(
            handle,
            attribute,
            out var actual,
            Marshal.SizeOf<int>());
        Assert(result >= 0, $"{subject} query failed with HRESULT 0x{result:X8}");
        Assert(
            actual == expected,
            $"{subject}: expected 0x{unchecked((uint)expected):X8}, actual 0x{unchecked((uint)actual):X8}");
    }

    private static void AssertOptionalDwmAttribute(
        IntPtr handle,
        int attribute,
        int expected,
        string subject)
    {
        var result = DwmGetWindowAttribute(
            handle,
            attribute,
            out var actual,
            Marshal.SizeOf<int>());
        if (result < 0)
        {
            Console.WriteLine($"{subject} query unavailable (HRESULT 0x{result:X8}); verify the native screenshot.");
            return;
        }

        Assert(
            actual == expected,
            $"{subject}: expected 0x{unchecked((uint)expected):X8}, actual 0x{unchecked((uint)actual):X8}");
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string name, params object?[] arguments)
    {
        var method = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate =>
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
            });
        method.Invoke(target, arguments);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SyntheticBackdropWindow : Window
    {
        private readonly Border _left;
        private readonly Border _right;

        public SyntheticBackdropWindow(Rect workArea)
        {
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            _left = new Border();
            _right = new Border();
            Grid.SetColumn(_right, 1);
            grid.Children.Add(_left);
            grid.Children.Add(_right);
            Content = grid;
        }

        public void UsePaletteA()
        {
            _left.Background = Brush(0xD0, 0x20, 0x40);
            _right.Background = Brush(0x20, 0x40, 0xD0);
        }

        public void UsePaletteB()
        {
            _left.Background = Brush(0x20, 0xB0, 0x60);
            _right.Background = Brush(0xD0, 0xB0, 0x20);
        }

        public void UseWhite()
        {
            _left.Background = Media.Brushes.White;
            _right.Background = Media.Brushes.White;
        }

        public void UseDark()
        {
            _left.Background = Brush(0x10, 0x12, 0x16);
            _right.Background = Brush(0x10, 0x12, 0x16);
        }

        public void UsePattern()
        {
            var drawing = new Media.DrawingGroup();
            drawing.Children.Add(new Media.GeometryDrawing(
                Brush(0x18, 0x1A, 0x20),
                null,
                new Media.RectangleGeometry(new Rect(0d, 0d, 32d, 32d))));
            drawing.Children.Add(new Media.GeometryDrawing(
                Brush(0xF0, 0xF2, 0xF4),
                null,
                new Media.RectangleGeometry(new Rect(0d, 0d, 16d, 16d))));
            drawing.Children.Add(new Media.GeometryDrawing(
                Brush(0xD0, 0x20, 0x80),
                null,
                new Media.RectangleGeometry(new Rect(16d, 16d, 16d, 16d))));
            var brush = new Media.DrawingBrush(drawing)
            {
                TileMode = Media.TileMode.Tile,
                Viewbox = new Rect(0d, 0d, 32d, 32d),
                ViewboxUnits = Media.BrushMappingMode.Absolute,
                Viewport = new Rect(0d, 0d, 32d, 32d),
                ViewportUnits = Media.BrushMappingMode.Absolute
            };
            brush.Freeze();
            _left.Background = brush;
            _right.Background = brush;
        }

        private static Media.Brush Brush(byte red, byte green, byte blue)
        {
            var brush = new Media.SolidColorBrush(Media.Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }
    }

    private sealed class CapturePair(
        Drawing.Bitmap paletteA,
        Drawing.Bitmap paletteB,
        double difference) : IDisposable
    {
        public double Difference { get; } = difference;

        public void Dispose()
        {
            paletteA.Dispose();
            paletteB.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        out int attributeValue,
        int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
