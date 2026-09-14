using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using Media = System.Windows.Media;
using WpfApplication = System.Windows.Application;

namespace WeekCalendarTray;

internal static class ThemeManager
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";
    public const int DefaultAcrylicOpacityPercent = 75;
    public const int MinAcrylicOpacityPercent = 20;
    public const int MaxAcrylicOpacityPercent = 95;
    private static bool _initialized;
    private static bool _windowClassHandlerRegistered;
    private static int _acrylicPreferenceVersion;

    public static event EventHandler? ThemeChanged;

    public static Task Initialization { get; private set; } = Task.CompletedTask;

    public static bool IsLightTheme { get; private set; }

    public static bool IsAcrylicEnabled { get; private set; }

    public static int AcrylicOpacityPercent { get; private set; } = DefaultAcrylicOpacityPercent;

    public static AppThemePreference ThemePreference { get; private set; } = AppThemePreference.System;

    public static Media.Color AccentColor { get; private set; }

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        if (!_windowClassHandlerRegistered)
        {
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(Window_Loaded));
            _windowClassHandlerRegistered = true;
        }

        WpfApplication.Current.Activated += Application_Activated;
        ApplyCurrentTheme();
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        Initialization = LoadAcrylicPreferenceAsync(_acrylicPreferenceVersion);
    }

    public static void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        WpfApplication.Current.Activated -= Application_Activated;
        AcrylicWindowManager.Shutdown();
        _initialized = false;
    }

    public static void SetAcrylicEnabled(bool enabled)
    {
        SetAcrylicOptions(enabled, AcrylicOpacityPercent);
    }

    public static void SetAcrylicOpacity(int opacityPercent)
    {
        SetAcrylicOptions(IsAcrylicEnabled, opacityPercent);
    }

    public static void SetAcrylicOptions(bool enabled, int opacityPercent)
    {
        SetAppearanceOptions(enabled, opacityPercent, ThemePreference);
    }

    public static void SetAppearanceOptions(
        bool enabled,
        int opacityPercent,
        AppThemePreference themePreference)
    {
        var dispatcher = WpfApplication.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke((Action)(() =>
                SetAppearanceOptions(enabled, opacityPercent, themePreference)));
            return;
        }

        _acrylicPreferenceVersion++;
        IsAcrylicEnabled = enabled;
        AcrylicOpacityPercent = Math.Clamp(
            opacityPercent,
            MinAcrylicOpacityPercent,
            MaxAcrylicOpacityPercent);
        ThemePreference = Enum.IsDefined(themePreference)
            ? themePreference
            : AppThemePreference.System;
        ApplyCurrentTheme();
    }

    public static void ApplyCurrentTheme()
    {
        IsLightTheme = ThemePreference switch
        {
            AppThemePreference.Light => true,
            AppThemePreference.Dark => false,
            _ => ReadWindowsAppTheme()
        };
        AccentColor = ReadWindowsAccentColor();
        var resources = WpfApplication.Current.Resources;

        if (IsLightTheme)
        {
            ApplyLightTheme(resources, AccentColor);
        }
        else
        {
            ApplyDarkTheme(resources, AccentColor);
        }

        ApplyAcrylicToOpenWindows();
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static async Task LoadAcrylicPreferenceAsync(int preferenceVersion)
    {
        try
        {
            var settings = await new SyncSettingsStore().LoadAsync();
            var dispatcher = WpfApplication.Current.Dispatcher;
            await dispatcher.InvokeAsync(() =>
            {
                if (_initialized && preferenceVersion == _acrylicPreferenceVersion)
                {
                    IsAcrylicEnabled = settings.AcrylicEnabled;
                    AcrylicOpacityPercent = Math.Clamp(
                        settings.AcrylicOpacityPercent,
                        MinAcrylicOpacityPercent,
                        MaxAcrylicOpacityPercent);
                    ThemePreference = AppThemePreferences.Parse(settings.ThemePreference);
                    ApplyCurrentTheme();
                }
            });
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("Load acrylic preference", ex);
        }
    }

    private static void Application_Activated(object? sender, EventArgs e)
    {
        ApplyAcrylicToOpenWindows();
    }

    private static void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized && sender is Window window)
        {
            PrepareWindow(window);
        }
    }

    public static void PrepareWindow(Window window)
    {
        AcrylicWindowManager.Apply(window, IsAcrylicEnabled && !SystemParameters.HighContrast,
            useDarkMode: !IsLightTheme, AccentColor, AcrylicOpacityPercent);
    }

    private static void ApplyAcrylicToOpenWindows()
    {
        if (WpfApplication.Current is null)
        {
            return;
        }

        var enabled = IsAcrylicEnabled && !SystemParameters.HighContrast;
        foreach (Window window in WpfApplication.Current.Windows)
        {
            AcrylicWindowManager.Apply(
                window,
                enabled,
                useDarkMode: !IsLightTheme,
                AccentColor,
                AcrylicOpacityPercent);
        }
    }

    private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        QueueSystemAppearanceRefresh("Apply Windows appearance preference");
    }

    private static void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        QueueSystemAppearanceRefresh("Apply display settings change");
    }

    private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            QueueSystemAppearanceRefresh("Apply resume appearance");
        }
    }

    private static void QueueSystemAppearanceRefresh(string operation)
    {
        var application = WpfApplication.Current;
        if (!_initialized || application is null)
        {
            return;
        }

        var dispatcher = application.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            if (dispatcher.CheckAccess())
            {
                ApplyCurrentTheme();
                return;
            }

            dispatcher.BeginInvoke((Action)(() =>
            {
                if (_initialized && !dispatcher.HasShutdownStarted)
                {
                    ApplyCurrentTheme();
                }
            }));
        }
        catch (InvalidOperationException ex)
        {
            AppDiagnostics.Log(operation, ex);
        }
    }

    private static bool ReadWindowsAppTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, writable: false);
            return key?.GetValue(AppsUseLightThemeValue) is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static Media.Color ReadWindowsAccentColor()
    {
        try
        {
            if (DwmGetColorizationColor(out var colorization, out _) >= 0)
            {
                return Media.Color.FromRgb(
                    (byte)(colorization >> 16),
                    (byte)(colorization >> 8),
                    (byte)colorization);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException
            or EntryPointNotFoundException
            or ExternalException)
        {
            AppDiagnostics.Log("Read Windows accent color", ex);
        }

        return IsLightTheme
            ? Media.Color.FromRgb(0x0A, 0x7F, 0x75)
            : Media.Color.FromRgb(0x2C, 0xF4, 0xD3);
    }

    private static void ApplyDarkTheme(ResourceDictionary resources, Media.Color accentColor)
    {
        var glassEnabled = IsAcrylicEnabled && !SystemParameters.HighContrast;
        SetBrush(resources, "WindowPanelBrush", 0x20, 0x21, 0x24);
        SetBrush(resources, "WindowSurfaceBrush", 0x20, 0x21, 0x24);
        SetAdaptiveBrush(resources, "GroupSurfaceBrush", 0x2B, 0x2D, 0x31, accentColor, 0.08d, 20);
        SetBrush(resources, "WindowBorderBrush", 0x3A, 0x3C, 0x40);
        SetBrush(resources, "PrimaryTextBrush", glassEnabled ? (byte)0xF5 : (byte)0xF1, glassEnabled ? (byte)0xF6 : (byte)0xF3, glassEnabled ? (byte)0xF7 : (byte)0xF4);
        SetBrush(resources, "SecondaryTextBrush", glassEnabled ? (byte)0xE4 : (byte)0xA6, glassEnabled ? (byte)0xE6 : (byte)0xAB, glassEnabled ? (byte)0xE9 : (byte)0xB4);
        SetBrush(resources, "HeaderTextBrush", glassEnabled ? (byte)0xEA : (byte)0xC7, glassEnabled ? (byte)0xEB : (byte)0xCB, glassEnabled ? (byte)0xED : (byte)0xD1);
        SetBrush(resources, "MutedTextBrush", glassEnabled ? (byte)0xDF : (byte)0x9A, glassEnabled ? (byte)0xE1 : (byte)0xA0, glassEnabled ? (byte)0xE4 : (byte)0xA6);
        SetBrush(resources, "SubtleTextBrush", glassEnabled ? (byte)0xD3 : (byte)0x85, glassEnabled ? (byte)0xD6 : (byte)0x8B, glassEnabled ? (byte)0xDA : (byte)0x94);
        SetBrush(resources, "OutsideMonthTextBrush", glassEnabled ? (byte)0xC4 : (byte)0x7B, glassEnabled ? (byte)0xC8 : (byte)0x7F, glassEnabled ? (byte)0xCE : (byte)0x87);
        SetAdaptiveBrush(resources, "ButtonBackgroundBrush", 0x30, 0x31, 0x34, accentColor, 0.05d, 18);
        SetBrush(resources, "ButtonBorderBrush", 0x46, 0x48, 0x4D);
        SetBrush(resources, "ButtonHoverBackgroundBrush", 0x3A, 0x3D, 0x42);
        SetBrush(resources, "ButtonPressedBackgroundBrush", 0x46, 0x4A, 0x50);
        SetAdaptiveBrush(resources, "InputBackgroundBrush", 0x2B, 0x2D, 0x31, accentColor, 0.05d, 25);
        SetBrush(resources, "DayHoverBackgroundBrush", 0x34, 0x37, 0x3C);
        SetBrush(resources, "AgendaHoverBackgroundBrush", 0x2A, 0x2D, 0x31);
        SetBrush(resources, "DividerBrush", 0x35, 0x37, 0x3B);
        SetBrush(resources, "WeekDividerBrush", 0x46, 0x48, 0x4D);
        SetBrush(resources, "AccentBrush", accentColor.R, accentColor.G, accentColor.B);
        SetAccentTextBrush(resources, accentColor);
        resources["PopupShadowColor"] = Media.Color.FromRgb(0x00, 0x00, 0x00);
        resources["PopupShadowOpacity"] = 0.35d;
    }

    private static void ApplyLightTheme(ResourceDictionary resources, Media.Color accentColor)
    {
        var glassEnabled = IsAcrylicEnabled && !SystemParameters.HighContrast;
        SetBrush(resources, "WindowPanelBrush", 0xFF, 0xFF, 0xFF);
        SetBrush(resources, "WindowSurfaceBrush", 0xFF, 0xFF, 0xFF);
        SetAdaptiveBrush(resources, "GroupSurfaceBrush", 0xF7, 0xF9, 0xFB, accentColor, 0.05d, 20);
        SetBrush(resources, "WindowBorderBrush", 0xD9, 0xDE, 0xE7);
        SetBrush(resources, "PrimaryTextBrush", 0x1F, 0x23, 0x28);
        SetBrush(resources, "SecondaryTextBrush", glassEnabled ? (byte)0x35 : (byte)0x4F, glassEnabled ? (byte)0x3D : (byte)0x5B, glassEnabled ? (byte)0x46 : (byte)0x67);
        SetBrush(resources, "HeaderTextBrush", glassEnabled ? (byte)0x38 : (byte)0x59, glassEnabled ? (byte)0x40 : (byte)0x63, glassEnabled ? (byte)0x49 : (byte)0x6E);
        SetBrush(resources, "MutedTextBrush", glassEnabled ? (byte)0x42 : (byte)0x6E, glassEnabled ? (byte)0x4A : (byte)0x77, glassEnabled ? (byte)0x53 : (byte)0x81);
        SetBrush(resources, "SubtleTextBrush", glassEnabled ? (byte)0x4B : (byte)0x6E, glassEnabled ? (byte)0x53 : (byte)0x77, glassEnabled ? (byte)0x5C : (byte)0x81);
        SetBrush(resources, "OutsideMonthTextBrush", glassEnabled ? (byte)0x60 : (byte)0x9A, glassEnabled ? (byte)0x69 : (byte)0xA3, glassEnabled ? (byte)0x74 : (byte)0xAE);
        SetAdaptiveBrush(resources, "ButtonBackgroundBrush", 0xF3, 0xF5, 0xF7, accentColor, 0.04d, 18);
        SetBrush(resources, "ButtonBorderBrush", 0xCC, 0xD3, 0xDC);
        SetBrush(resources, "ButtonHoverBackgroundBrush", 0xE8, 0xEC, 0xF1);
        SetBrush(resources, "ButtonPressedBackgroundBrush", 0xDD, 0xE3, 0xEA);
        SetAdaptiveBrush(resources, "InputBackgroundBrush", 0xF7, 0xF9, 0xFB, accentColor, 0.03d, 25);
        SetBrush(resources, "DayHoverBackgroundBrush", 0xEE, 0xF3, 0xF6);
        SetBrush(resources, "AgendaHoverBackgroundBrush", 0xF4, 0xF7, 0xFA);
        SetBrush(resources, "DividerBrush", 0xE1, 0xE6, 0xED);
        SetBrush(resources, "WeekDividerBrush", 0xD3, 0xDA, 0xE3);
        SetBrush(resources, "AccentBrush", accentColor.R, accentColor.G, accentColor.B);
        SetAccentTextBrush(resources, accentColor);
        resources["PopupShadowColor"] = Media.Color.FromRgb(0x00, 0x00, 0x00);
        resources["PopupShadowOpacity"] = 0.18d;
    }

    private static void SetBrush(ResourceDictionary resources, string key, byte red, byte green, byte blue)
    {
        resources[key] = new Media.SolidColorBrush(Media.Color.FromRgb(red, green, blue));
    }

    private static void SetAdaptiveBrush(
        ResourceDictionary resources,
        string key,
        byte red,
        byte green,
        byte blue,
        Media.Color accentColor,
        double accentWeight,
        int opacityOffset)
    {
        var alphaPercent = IsAcrylicEnabled && !SystemParameters.HighContrast
            ? Math.Clamp(AcrylicOpacityPercent + opacityOffset, MinAcrylicOpacityPercent, 100)
            : 100;
        resources[key] = new Media.SolidColorBrush(Media.Color.FromArgb(
            (byte)Math.Round(alphaPercent * 2.55d),
            BlendChannel(red, accentColor.R, accentWeight),
            BlendChannel(green, accentColor.G, accentWeight),
            BlendChannel(blue, accentColor.B, accentWeight)));
    }

    private static void SetAccentTextBrush(ResourceDictionary resources, Media.Color accentColor)
    {
        var luminance = RelativeLuminance(accentColor);
        var blackContrast = (luminance + 0.05d) / 0.05d;
        var whiteContrast = 1.05d / (luminance + 0.05d);
        var foreground = blackContrast >= whiteContrast ? Media.Colors.Black : Media.Colors.White;
        resources["AccentTextBrush"] = new Media.SolidColorBrush(foreground);
    }

    private static double RelativeLuminance(Media.Color color)
    {
        static double Linearize(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045d
                ? value / 12.92d
                : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        return (0.2126d * Linearize(color.R))
            + (0.7152d * Linearize(color.G))
            + (0.0722d * Linearize(color.B));
    }

    private static byte BlendChannel(byte baseChannel, byte accentChannel, double accentWeight)
    {
        return (byte)Math.Round(
            (baseChannel * (1d - accentWeight)) + (accentChannel * accentWeight));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(
        out uint colorization,
        [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);
}
