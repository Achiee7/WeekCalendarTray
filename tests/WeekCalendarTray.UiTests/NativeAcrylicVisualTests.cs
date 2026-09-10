using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
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
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = workArea.Left + Math.Max(0d, (workArea.Width - window.Width) / 2d);
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
                artifactDirectory);

            ThemeManager.SetAcrylicEnabled(false);
            ThemeManager.ApplyCurrentTheme();
            FlushDesktop(window);

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
        string artifactDirectory)
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
                "MainWindow-native-acrylic-backdrop-a.png"));
            backdrop.UsePaletteB();
            FlushDesktop(backdrop);
            second = CaptureWindow(window, Path.Combine(
                artifactDirectory,
                "MainWindow-native-acrylic-backdrop-b.png"));
            difference = MeanPixelDifference(first, second, ComparisonInset);
            if (difference >= 2d)
            {
                break;
            }

            Thread.Sleep(200);
        }

        return new CapturePair(first!, second!, difference);
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
}
