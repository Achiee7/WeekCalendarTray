namespace WeekCalendarTray.Core;

public sealed class PrayerTimesCalculator
{
    private const double SunriseZenith = 90.833d;
    private const double FajrAngle = 15d;

    public DailyPrayerTimes Calculate(DateOnly date, PrayerTimesLocation location, TimeZoneInfo timeZone)
    {
        var solar = CalculateSolarTimes(date, location, timeZone);
        var fajr = CalculateFajrTime(date, location, timeZone, solar);
        var asr = CalculateAsrTime(date, location, timeZone, solar);
        var isha = solar.Sunset.AddMinutes(GetIshaOffsetMinutes(date));

        var prayers = new[]
        {
            new PrayerTime("Fajr", fajr),
            new PrayerTime("Shuruq", solar.Sunrise),
            new PrayerTime("Dhor", solar.Noon),
            new PrayerTime("Asr", asr),
            new PrayerTime("Maghrib", solar.Sunset),
            new PrayerTime("Isha", isha)
        };

        return new DailyPrayerTimes(date, location, prayers);
    }

    private static DateTimeOffset CalculateAsrTime(
        DateOnly date,
        PrayerTimesLocation location,
        TimeZoneInfo timeZone,
        SolarTimes solar)
    {
        var latitude = ToRadians(location.Latitude);
        var declination = ToRadians(solar.DeclinationDegrees);
        var altitude = ToDegrees(Math.Atan(1d / (1d + Math.Tan(Math.Abs(latitude - declination)))));
        var zenith = 90d - altitude;
        var afternoon = CalculateSunTime(date, location, timeZone, zenith, afterNoon: true);

        return afternoon ?? solar.Noon.AddHours(4);
    }

    private static DateTimeOffset CalculateFajrTime(
        DateOnly date,
        PrayerTimesLocation location,
        TimeZoneInfo timeZone,
        SolarTimes solar)
    {
        var twilightFajr = CalculateTwilightTime(date, location, timeZone, FajrAngle, beforeSunrise: true);
        var adjustedFajr = solar.Sunrise.AddMinutes(-GetFajrOffsetMinutes(date));

        if (twilightFajr is null)
        {
            return adjustedFajr;
        }

        return Math.Abs(location.Latitude) >= 48d && adjustedFajr > twilightFajr.Value
            ? adjustedFajr
            : twilightFajr.Value;
    }

    private static SolarTimes CalculateSolarTimes(DateOnly date, PrayerTimesLocation location, TimeZoneInfo timeZone)
    {
        var solar = CalculateSolarPosition(date);
        var offset = timeZone.GetUtcOffset(date.ToDateTime(new TimeOnly(12, 0)));
        var noonMinutes = 720d - (4d * location.Longitude) - solar.EquationOfTimeMinutes + offset.TotalMinutes;
        var sunrise = CalculateSunTime(date, location, timeZone, SunriseZenith, afterNoon: false) ?? ToDateTimeOffset(date, noonMinutes - 360, timeZone);
        var sunset = CalculateSunTime(date, location, timeZone, SunriseZenith, afterNoon: true) ?? ToDateTimeOffset(date, noonMinutes + 360, timeZone);

        return new SolarTimes(
            ToDateTimeOffset(date, noonMinutes, timeZone),
            sunrise,
            sunset,
            solar.DeclinationDegrees);
    }

    private static DateTimeOffset? CalculateTwilightTime(
        DateOnly date,
        PrayerTimesLocation location,
        TimeZoneInfo timeZone,
        double sunAngle,
        bool beforeSunrise)
    {
        return CalculateSunTime(date, location, timeZone, 90d + sunAngle, afterNoon: !beforeSunrise);
    }

    private static DateTimeOffset? CalculateSunTime(
        DateOnly date,
        PrayerTimesLocation location,
        TimeZoneInfo timeZone,
        double zenith,
        bool afterNoon)
    {
        var solar = CalculateSolarPosition(date);
        var offset = timeZone.GetUtcOffset(date.ToDateTime(new TimeOnly(12, 0)));
        var noonMinutes = 720d - (4d * location.Longitude) - solar.EquationOfTimeMinutes + offset.TotalMinutes;
        var latitude = ToRadians(location.Latitude);
        var declination = ToRadians(solar.DeclinationDegrees);
        var cosHourAngle = (Math.Cos(ToRadians(zenith)) / (Math.Cos(latitude) * Math.Cos(declination)))
            - (Math.Tan(latitude) * Math.Tan(declination));

        if (cosHourAngle is < -1d or > 1d)
        {
            return null;
        }

        var hourAngleMinutes = ToDegrees(Math.Acos(cosHourAngle)) * 4d;
        var minutes = afterNoon
            ? noonMinutes + hourAngleMinutes
            : noonMinutes - hourAngleMinutes;

        return ToDateTimeOffset(date, minutes, timeZone);
    }

    private static SolarPosition CalculateSolarPosition(DateOnly date)
    {
        var dayAngle = (2d * Math.PI / 365d) * (date.DayOfYear - 1);
        var equationOfTime = 229.18d * (
            0.000075d
            + (0.001868d * Math.Cos(dayAngle))
            - (0.032077d * Math.Sin(dayAngle))
            - (0.014615d * Math.Cos(2d * dayAngle))
            - (0.040849d * Math.Sin(2d * dayAngle)));
        var declination = 0.006918d
            - (0.399912d * Math.Cos(dayAngle))
            + (0.070257d * Math.Sin(dayAngle))
            - (0.006758d * Math.Cos(2d * dayAngle))
            + (0.000907d * Math.Sin(2d * dayAngle))
            - (0.002697d * Math.Cos(3d * dayAngle))
            + (0.00148d * Math.Sin(3d * dayAngle));

        return new SolarPosition(equationOfTime, ToDegrees(declination));
    }

    private static double GetIshaOffsetMinutes(DateOnly date)
    {
        var seasonalAngle = (2d * Math.PI * (date.DayOfYear - 172)) / 365.2425d;
        return 100d + (5d * Math.Cos(seasonalAngle));
    }

    private static double GetFajrOffsetMinutes(DateOnly date)
    {
        var seasonalAngle = (2d * Math.PI * (date.DayOfYear - 172)) / 365.2425d;
        return 95d + (15d * Math.Cos(seasonalAngle));
    }

    private static DateTimeOffset ToDateTimeOffset(DateOnly date, double minutesAfterMidnight, TimeZoneInfo timeZone)
    {
        var roundedMinutes = (int)Math.Round(minutesAfterMidnight, MidpointRounding.AwayFromZero);
        var dayOffset = Math.DivRem(roundedMinutes, 1440, out var minuteOfDay);
        if (minuteOfDay < 0)
        {
            dayOffset--;
            minuteOfDay += 1440;
        }

        var localDate = date.AddDays(dayOffset);
        var localDateTime = localDate.ToDateTime(TimeOnly.MinValue).AddMinutes(minuteOfDay);
        return new DateTimeOffset(localDateTime, timeZone.GetUtcOffset(localDateTime));
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180d;
    }

    private static double ToDegrees(double radians)
    {
        return radians * 180d / Math.PI;
    }

    private sealed record SolarPosition(
        double EquationOfTimeMinutes,
        double DeclinationDegrees);

    private sealed record SolarTimes(
        DateTimeOffset Noon,
        DateTimeOffset Sunrise,
        DateTimeOffset Sunset,
        double DeclinationDegrees);
}
