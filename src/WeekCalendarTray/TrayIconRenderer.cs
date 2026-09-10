using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using WeekCalendarTray.Core;

namespace WeekCalendarTray;

internal static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Create(DateTime now)
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var isLightTheme = ThemeManager.IsLightTheme;
        using var background = new SolidBrush(isLightTheme
            ? Color.FromArgb(255, 255, 255, 255)
            : Color.FromArgb(255, 32, 33, 36));
        using var accent = new SolidBrush(isLightTheme
            ? Color.FromArgb(255, 10, 127, 117)
            : Color.FromArgb(255, 44, 244, 211));
        using var primaryText = new SolidBrush(isLightTheme
            ? Color.FromArgb(255, 31, 35, 40)
            : Color.FromArgb(255, 241, 243, 244));
        using var accentText = new SolidBrush(isLightTheme
            ? Color.FromArgb(255, 255, 255, 255)
            : Color.FromArgb(255, 16, 18, 20));
        using var border = new Pen(isLightTheme
            ? Color.FromArgb(255, 217, 222, 231)
            : Color.FromArgb(255, 70, 74, 80), 2);
        using var dayFont = new Font("Segoe UI", 25, FontStyle.Bold, GraphicsUnit.Pixel);
        using var weekFont = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);

        graphics.FillRectangle(background, 2, 2, 60, 60);
        graphics.DrawRectangle(border, 2, 2, 60, 60);
        graphics.FillRectangle(accent, 2, 42, 60, 20);

        DrawCentered(graphics, now.Day.ToString(CultureInfo.CurrentCulture), dayFont, primaryText, new RectangleF(2, 5, 60, 35));
        DrawCentered(graphics, $"W{CalendarGridBuilder.GetIsoWeekNumber(DateOnly.FromDateTime(now)):00}", weekFont, accentText, new RectangleF(2, 42, 60, 19));

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawCentered(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None
        };

        graphics.DrawString(text, font, brush, bounds, format);
    }
}
