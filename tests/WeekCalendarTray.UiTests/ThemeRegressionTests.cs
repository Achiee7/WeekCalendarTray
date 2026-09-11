using System.Windows;
using Media = System.Windows.Media;

namespace WeekCalendarTray.UiTests;

internal static class ThemeRegressionTests
{
    public static void Run()
    {
        var previousEnabled = ThemeManager.IsAcrylicEnabled;
        var previousOpacity = ThemeManager.AcrylicOpacityPercent;
        var previousTheme = ThemeManager.ThemePreference;
        try
        {
            ThemeManager.SetAppearanceOptions(true, int.MinValue, AppThemePreference.Dark);
            Program.Assert(ThemeManager.AcrylicOpacityPercent == ThemeManager.MinAcrylicOpacityPercent,
                "acrylic opacity did not clamp to its minimum");
            Program.Assert(!ThemeManager.IsLightTheme, "Dark preference did not force the dark theme");
            var lowAlpha = ResourceBrush("GroupSurfaceBrush").Color.A;
            Program.Assert(ThemeManager.AcrylicOpacityPercent == 20, "minimum opacity must be 20 percent");

            ThemeManager.SetAppearanceOptions(true, int.MaxValue, AppThemePreference.Light);
            Program.Assert(ThemeManager.AcrylicOpacityPercent == ThemeManager.MaxAcrylicOpacityPercent,
                "acrylic opacity did not clamp to its maximum");
            Program.Assert(ThemeManager.IsLightTheme, "Light preference did not force the light theme");
            var highAlpha = ResourceBrush("GroupSurfaceBrush").Color.A;
            Program.Assert(lowAlpha < highAlpha, "acrylic tint resources did not respond to opacity changes");

            var accent = ResourceBrush("AccentBrush").Color;
            var accentText = ResourceBrush("AccentTextBrush").Color;
            Program.Assert(accent == ThemeManager.AccentColor, "AccentBrush did not use the Windows accent color");
            Program.Assert(ContrastRatio(accent, accentText) >= 4.5d,
                "adaptive accent text did not retain 4.5:1 contrast");

            var store = new SyncSettingsStore();
            var restored = Task.Run(async () =>
                {
                    await store.SaveAsync(new SyncSettings
                    {
                        AcrylicEnabled = true,
                        AcrylicOpacityPercent = 20,
                        ThemePreference = nameof(AppThemePreference.Dark)
                    });
                    return await store.LoadAsync();
                })
                .GetAwaiter()
                .GetResult();
            Program.Assert(restored.AcrylicEnabled, "acrylic enabled state did not persist");
            Program.Assert(restored.AcrylicOpacityPercent == 20, "20 percent acrylic opacity did not persist");
            Program.Assert(restored.ThemePreference == nameof(AppThemePreference.Dark),
                "theme preference did not persist");

            restored.ThemePreference = "invalid-test-value";
            restored = Task.Run(async () =>
                {
                    await store.SaveAsync(restored);
                    return await store.LoadAsync();
                })
                .GetAwaiter()
                .GetResult();
            Program.Assert(restored.ThemePreference == nameof(AppThemePreference.System),
                "invalid theme preference did not fall back to System");

            ThemeManager.SetAppearanceOptions(false, 63, AppThemePreference.System);
            Program.Assert(ResourceBrush("GroupSurfaceBrush").Color.A == byte.MaxValue,
                "disabled acrylic left a translucent grouped surface");
        }
        finally
        {
            ThemeManager.SetAppearanceOptions(previousEnabled, previousOpacity, previousTheme);
        }
    }

    private static Media.SolidColorBrush ResourceBrush(string key) =>
        Application.Current.Resources[key] as Media.SolidColorBrush
        ?? throw new InvalidOperationException($"{key} is not a solid-color brush");

    private static double ContrastRatio(Media.Color first, Media.Color second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05d)
            / (Math.Min(firstLuminance, secondLuminance) + 0.05d);
    }

    private static double RelativeLuminance(Media.Color color)
    {
        static double Linearize(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        return (0.2126d * Linearize(color.R)) + (0.7152d * Linearize(color.G)) + (0.0722d * Linearize(color.B));
    }
}
