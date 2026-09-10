using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Media = System.Windows.Media;

namespace WeekCalendarTray;

internal static class AcrylicWindowManager
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmSystemBackdropNone = 1;
    private const int DwmSystemBackdropTransientWindow = 3;
    private const int WmNcActivate = 0x0086;
    private static readonly Dictionary<Window, AcrylicWindowRegistration> Registrations = [];

    public static void Apply(
        Window window,
        bool enabled,
        bool useDarkMode,
        Media.Color accentColor,
        int opacityPercent)
    {
        ApplyWindowFrame(window);
        if (!enabled
            || window.AllowsTransparency
            || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            Remove(window);
            return;
        }

        if (!Registrations.TryGetValue(window, out var registration))
        {
            registration = new AcrylicWindowRegistration(window, Remove);
            Registrations.Add(window, registration);
        }

        registration.Apply(useDarkMode, accentColor, opacityPercent);
    }

    private static void ApplyWindowFrame(Window window)
    {
        if (window.AllowsTransparency || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        // Round the native surface, not just the WPF border drawn inside it.
        var roundCorners = 2;
        var noBorderColor = unchecked((int)0xFFFFFFFE);
        _ = DwmSetWindowAttribute(handle, 33, ref roundCorners, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 34, ref noBorderColor, sizeof(int));
    }

    public static void Shutdown()
    {
        foreach (var registration in Registrations.Values.ToList())
        {
            registration.Dispose();
        }

        Registrations.Clear();
    }

    private static void Remove(Window window)
    {
        if (!Registrations.Remove(window, out var registration))
        {
            return;
        }

        registration.Dispose();
    }

    private sealed class AcrylicWindowRegistration : IDisposable
    {
        private readonly Window _window;
        private readonly Action<Window> _remove;
        private bool _backdropApplied;
        private bool _disposed;
        private bool _hasOriginalTargetBackground;
        private bool _hadWindowPanelBrush;
        private bool _windowPanelBrushCaptured;
        private bool _windowTintApplied;
        private object? _originalWindowPanelBrush;
        private Media.Color _originalTargetBackground;
        private Media.Color _accentColor;
        private int _opacityPercent;
        private bool _contentRenderedReapplyAvailable = true;
        private bool _postRenderApplyQueued;
        private bool _useDarkMode;
        private HwndSource? _windowSource;

        public AcrylicWindowRegistration(Window window, Action<Window> remove)
        {
            _window = window;
            _remove = remove;
            _window.Closed += Window_Closed;
            _window.ContentRendered += Window_ContentRendered;
            _window.IsVisibleChanged += Window_IsVisibleChanged;
            _window.SourceInitialized += Window_SourceInitialized;
            _window.Activated += Window_ActivationChanged;
            _window.Deactivated += Window_ActivationChanged;
        }

        public void Apply(bool useDarkMode, Media.Color accentColor, int opacityPercent)
        {
            if (_disposed)
            {
                return;
            }

            _useDarkMode = useDarkMode;
            _accentColor = accentColor;
            _opacityPercent = Math.Clamp(opacityPercent, 35, 95);
            var handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (HwndSource.FromHwnd(handle)?.CompositionTarget is not { } compositionTarget)
                {
                    return;
                }

                if (!_hasOriginalTargetBackground)
                {
                    _originalTargetBackground = compositionTarget.BackgroundColor;
                    _hasOriginalTargetBackground = true;
                }

                compositionTarget.BackgroundColor = Media.Colors.Transparent;
                var margins = Margins.ExtendThroughClientArea;
                if (DwmExtendFrameIntoClientArea(handle, ref margins) < 0)
                {
                    RestoreTargetBackground(compositionTarget);
                    return;
                }

                var darkMode = useDarkMode ? 1 : 0;
                _ = DwmSetWindowAttribute(
                    handle,
                    DwmwaUseImmersiveDarkMode,
                    ref darkMode,
                    Marshal.SizeOf<int>());

                var backdrop = DwmSystemBackdropTransientWindow;
                var result = DwmSetWindowAttribute(
                    handle,
                    DwmwaSystemBackdropType,
                    ref backdrop,
                    Marshal.SizeOf<int>());
                if (result < 0)
                {
                    ResetExtendedFrame(handle);
                    RestoreTargetBackground(compositionTarget);
                    return;
                }

                _backdropApplied = true;
                ApplyWindowTint();
                if (_window.WindowStyle == WindowStyle.None)
                {
                    AttachPresentationHook(HwndSource.FromHwnd(handle));
                    if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
                    {
                        var groupActive = _window.IsActive || IsSameWindowGroup(handle, GetForegroundWindow());
                        _ = DefWindowProc(handle, WmNcActivate,
                            groupActive ? new IntPtr(1) : IntPtr.Zero,
                            groupActive ? new IntPtr(-1) : IntPtr.Zero);
                    }
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException
                or EntryPointNotFoundException
                or ExternalException)
            {
                if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } compositionTarget)
                {
                    RestoreTargetBackground(compositionTarget);
                }

                ResetExtendedFrame(handle);
                RestoreWindowTint();
                AppDiagnostics.Log("Apply acrylic backdrop", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _window.Closed -= Window_Closed;
            _window.ContentRendered -= Window_ContentRendered;
            _window.IsVisibleChanged -= Window_IsVisibleChanged;
            _window.SourceInitialized -= Window_SourceInitialized;
            _window.Activated -= Window_ActivationChanged;
            _window.Deactivated -= Window_ActivationChanged;
            if (_windowSource is not null)
            {
                _windowSource.RemoveHook(WindowProc);
                _windowSource = null;
            }
            RestoreWindowTint();

            var handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } compositionTarget)
            {
                RestoreTargetBackground(compositionTarget);
            }

            try
            {
                if (_backdropApplied)
                {
                    var backdrop = DwmSystemBackdropNone;
                    _ = DwmSetWindowAttribute(
                        handle,
                        DwmwaSystemBackdropType,
                        ref backdrop,
                        Marshal.SizeOf<int>());
                }

                ResetExtendedFrame(handle);
                if (_backdropApplied && _window.IsVisible)
                {
                    _ = DefWindowProc(handle, WmNcActivate,
                        _window.IsActive ? new IntPtr(1) : IntPtr.Zero,
                        _window.IsActive ? new IntPtr(-1) : IntPtr.Zero);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException
                or EntryPointNotFoundException
                or ExternalException)
            {
                AppDiagnostics.Log("Remove acrylic backdrop", ex);
            }
        }

        private void AttachPresentationHook(HwndSource? source)
        {
            if (source is null || ReferenceEquals(source, _windowSource))
            {
                return;
            }

            _windowSource?.RemoveHook(WindowProc);
            _windowSource = source;
            _windowSource.AddHook(WindowProc);
        }

        private IntPtr WindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!_disposed && _backdropApplied && message == WmNcActivate
                && wParam == IntPtr.Zero && _window.IsVisible
                && _window.WindowStyle == WindowStyle.None
                && _window.WindowState != WindowState.Minimized
                && IsSameWindowGroup(handle, lParam))
            {
                // Preserve glass within the owned-window group, not input focus. Leave other apps alone.
                _ = DefWindowProc(handle, message, new IntPtr(1), new IntPtr(-1));
                handled = true;
                return new IntPtr(1); // Allow the real activation change to complete.
            }

            return IntPtr.Zero;
        }

        private static bool IsSameWindowGroup(IntPtr handle, IntPtr target)
        {
            if (target == IntPtr.Zero || target == new IntPtr(-1))
            {
                return false;
            }

            var owner = FindRegisteredRootOwner(handle);
            return owner is not null && ReferenceEquals(owner, FindRegisteredRootOwner(target));
        }

        private static Window? FindRegisteredRootOwner(IntPtr handle)
        {
            // Use WPF ownership: hidden taskbar-owner HWNDs are not application window groups.
            var window = Registrations.Keys.FirstOrDefault(candidate =>
                new WindowInteropHelper(candidate).Handle == handle);
            while (window?.Owner is { } owner)
            {
                window = owner;
            }

            return window;
        }

        private void ApplyWindowTint()
        {
            if (!_windowPanelBrushCaptured)
            {
                _hadWindowPanelBrush = _window.Resources.Contains("WindowSurfaceBrush");
                if (_hadWindowPanelBrush)
                {
                    _originalWindowPanelBrush = _window.Resources["WindowSurfaceBrush"];
                }

                _windowPanelBrushCaptured = true;
            }

            var baseColor = _useDarkMode
                ? Media.Color.FromRgb(0x20, 0x21, 0x24)
                : Media.Colors.White;
            var accentWeight = _useDarkMode ? 0.04d : 0.02d;
            var color = Media.Color.FromArgb(
                (byte)Math.Round(_opacityPercent * 2.55d),
                BlendChannel(baseColor.R, _accentColor.R, accentWeight),
                BlendChannel(baseColor.G, _accentColor.G, accentWeight),
                BlendChannel(baseColor.B, _accentColor.B, accentWeight));
            var brush = new Media.SolidColorBrush(color);
            brush.Freeze();
            _window.Resources["WindowSurfaceBrush"] = brush;
            _windowTintApplied = true;
        }

        private void RestoreWindowTint()
        {
            if (!_windowTintApplied)
            {
                return;
            }

            if (_hadWindowPanelBrush)
            {
                _window.Resources["WindowSurfaceBrush"] = _originalWindowPanelBrush;
            }
            else
            {
                _window.Resources.Remove("WindowSurfaceBrush");
            }

            _windowTintApplied = false;
        }

        private void RestoreTargetBackground(HwndTarget compositionTarget)
        {
            if (_hasOriginalTargetBackground)
            {
                compositionTarget.BackgroundColor = _originalTargetBackground;
            }
        }

        private static void ResetExtendedFrame(IntPtr handle)
        {
            var margins = default(Margins);
            _ = DwmExtendFrameIntoClientArea(handle, ref margins);
        }

        private static byte BlendChannel(byte baseChannel, byte accentChannel, double accentWeight)
        {
            return (byte)Math.Round(
                (baseChannel * (1d - accentWeight)) + (accentChannel * accentWeight));
        }

        private void Window_ActivationChanged(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(_window).Handle;
            foreach (var registration in Registrations.Values.ToArray())
            {
                var target = new WindowInteropHelper(registration._window).Handle;
                if (IsSameWindowGroup(handle, target))
                {
                    registration.QueuePostRenderApply();
                }
            }
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            Apply(_useDarkMode, _accentColor, _opacityPercent);
        }

        private void Window_ContentRendered(object? sender, EventArgs e)
        {
            if (!_contentRenderedReapplyAvailable)
            {
                return;
            }

            _contentRenderedReapplyAvailable = false;
            QueuePostRenderApply();
        }

        private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                QueuePostRenderApply();
            }
            else
            {
                _contentRenderedReapplyAvailable = true;
            }
        }

        private void QueuePostRenderApply()
        {
            if (_disposed || _postRenderApplyQueued || !_window.IsVisible)
            {
                return;
            }

            _postRenderApplyQueued = true;
            _window.Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
            {
                _postRenderApplyQueued = false;
                if (!_disposed && _window.IsVisible)
                {
                    Apply(_useDarkMode, _accentColor, _opacityPercent);
                }
            }));
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _remove(_window);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;

        public static Margins ExtendThroughClientArea => new()
        {
            Left = -1,
            Right = -1,
            Top = -1,
            Bottom = -1
        };
    }

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(
        IntPtr windowHandle,
        ref Margins margins);
}
