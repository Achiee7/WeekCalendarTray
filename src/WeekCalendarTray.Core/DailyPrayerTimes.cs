namespace WeekCalendarTray.Core;

public sealed record DailyPrayerTimes(
    DateOnly Date,
    PrayerTimesLocation Location,
    IReadOnlyList<PrayerTime> Prayers);
