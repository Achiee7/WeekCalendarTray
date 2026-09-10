using System.Globalization;
using System.Windows;
using WeekCalendarTray.Core;

namespace WeekCalendarTray;

public sealed class PrayerTimeViewModel(PrayerTime prayerTime, bool isNext)
{
    public string Name => prayerTime.Name;

    public string TimeText => prayerTime.Time.LocalDateTime.ToString("HH:mm", CultureInfo.CurrentCulture);

    public bool IsNext => isNext;

    public Visibility NextVisibility => isNext ? Visibility.Visible : Visibility.Collapsed;
}
