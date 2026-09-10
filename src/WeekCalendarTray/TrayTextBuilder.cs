using System.Globalization;
using WeekCalendarTray.Core;

namespace WeekCalendarTray;

internal static class TrayTextBuilder
{
    public static string Build(DateTime now)
    {
        var week = CalendarGridBuilder.GetIsoWeekNumber(DateOnly.FromDateTime(now));
        var date = now.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture);
        var time = now.ToString("t", CultureInfo.CurrentCulture);
        return $"{date} | W{week:00} | {time}";
    }
}
