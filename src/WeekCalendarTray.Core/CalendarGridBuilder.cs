using System.Globalization;

namespace WeekCalendarTray.Core;

public static class CalendarGridBuilder
{
    private const int WeeksToRender = 6;
    private const int DaysPerWeek = 7;

    public static CalendarMonthGrid CreateMonth(DateOnly visibleMonth, DateOnly selectedDate, DateOnly today)
    {
        var month = FirstDayOfMonth(visibleMonth);
        var firstVisibleDate = month.AddDays(-DaysSinceMonday(month.DayOfWeek));
        var weeks = new List<CalendarWeek>(WeeksToRender);

        for (var weekIndex = 0; weekIndex < WeeksToRender; weekIndex++)
        {
            var weekStart = firstVisibleDate.AddDays(weekIndex * DaysPerWeek);
            var days = new List<CalendarDay>(DaysPerWeek);

            for (var dayIndex = 0; dayIndex < DaysPerWeek; dayIndex++)
            {
                var date = weekStart.AddDays(dayIndex);
                days.Add(new CalendarDay(
                    date,
                    date.Day,
                    GetIsoWeekNumber(date),
                    date.Year == month.Year && date.Month == month.Month,
                    date == today,
                    date == selectedDate));
            }

            weeks.Add(new CalendarWeek(GetIsoWeekNumber(weekStart), days));
        }

        return new CalendarMonthGrid(month, selectedDate, today, weeks);
    }

    public static DateOnly FirstDayOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    public static int GetIsoWeekNumber(DateOnly date)
    {
        return ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
    }

    private static int DaysSinceMonday(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => 0
        };
    }
}
