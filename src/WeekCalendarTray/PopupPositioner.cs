using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace WeekCalendarTray;

internal static class PopupPositioner
{
    private const double MarginSize = 12;

    public static Rect GetWorkArea(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var area = Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY,
            area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
    }

    public static void PlaceNearTaskbar(Window window)
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var workArea = screen.WorkingArea;
        var bounds = screen.Bounds;
        var dpi = VisualTreeHelper.GetDpi(window);

        var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        var width = window.Width > 0 ? window.Width : window.ActualWidth;
        var height = window.Height > 0 ? window.Height : window.ActualHeight;

        var workLeft = workArea.Left / scaleX;
        var workTop = workArea.Top / scaleY;
        var workRight = workArea.Right / scaleX;
        var workBottom = workArea.Bottom / scaleY;
        var boundsLeft = bounds.Left / scaleX;
        var boundsTop = bounds.Top / scaleY;
        var boundsRight = bounds.Right / scaleX;
        var boundsBottom = bounds.Bottom / scaleY;

        var taskbarAtTop = workTop > boundsTop;
        var taskbarAtLeft = workLeft > boundsLeft;
        var taskbarAtRight = workRight < boundsRight;
        var taskbarAtBottom = workBottom < boundsBottom;

        var left = workRight - width - MarginSize;
        var top = workBottom - height - MarginSize;

        if (taskbarAtTop)
        {
            top = workTop + MarginSize;
        }

        if (taskbarAtBottom)
        {
            top = workBottom - height - MarginSize;
        }

        if (taskbarAtLeft)
        {
            left = workLeft + MarginSize;
        }

        if (taskbarAtRight)
        {
            left = workRight - width - MarginSize;
        }

        window.Left = Clamp(left, workLeft + MarginSize, workRight - width - MarginSize);
        window.Top = Clamp(top, workTop + MarginSize, workBottom - height - MarginSize);
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(Math.Max(value, min), max);
    }
}
