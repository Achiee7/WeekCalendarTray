using System.Reflection;
using System.Windows.Threading;
using WeekCalendarTray.Core;

namespace WeekCalendarTray.UiTests;

internal static class PerformanceRegressionTests
{
    public static void Run()
    {
        var context = SynchronizationContext.Current;
        using var controller = new TrayApplicationController();
        // WinForms tray construction may install its own context; this harness
        // is driven by WPF dispatcher frames rather than a WinForms message loop.
        SynchronizationContext.SetSynchronizationContext(context);
        var store = new SyncSettingsStore();
        var original = store.LoadAsync().GetAwaiter().GetResult();
        try
        {
            Call(controller, "UpdateTrayStatus");
            var notify = Field<System.Windows.Forms.NotifyIcon>(controller, "_notifyIcon");
            var icon = notify.Icon;
            for (var i = 0; i < 10; i++) Call(controller, "UpdateTrayStatus");
            Program.Assert(ReferenceEquals(icon, notify.Icon), "unchanged tray status recreated its icon");
            Program.SetPrivateField(controller, "_trayIconDate", DateOnly.FromDateTime(DateTime.Now).AddDays(-1));
            Call(controller, "UpdateTrayStatus");
            Program.Assert(!ReferenceEquals(icon, notify.Icon), "date change did not refresh tray icon");

            var settings = store.LoadAsync().GetAwaiter().GetResult();
            settings.PrayerTimesEnabled = false;
            settings.PrayerNotificationsEnabled = false;
            store.SaveAsync(settings).GetAwaiter().GetResult();
            var first = Load(controller);
            var second = Load(controller);
            Program.Assert(ReferenceEquals(first, second), "notification settings were reloaded without invalidation");
            var coordinator = Field<CalendarSyncCoordinator>(controller, "_syncCoordinator");
            coordinator.NotifySettingsChanged();
            var third = Load(controller);
            Program.Assert(!ReferenceEquals(first, third), "settings change did not invalidate notification cache");

            var location = new PrayerTimesLocation("Synthetic", 51.9, 5.1);
            var date = DateOnly.FromDateTime(DateTime.Now);
            var timetable = Call(controller, "GetPrayerTimetable", date, location, TimeZoneInfo.Local);
            Program.Assert(ReferenceEquals(timetable, Call(controller, "GetPrayerTimetable", date, location, TimeZoneInfo.Local)),
                "notification timetable was recalculated without changes");
            Program.Assert(!ReferenceEquals(timetable, Call(controller, "GetPrayerTimetable", date.AddDays(1), location, TimeZoneInfo.Local)),
                "notification timetable was stale after date change");
            timetable = Call(controller, "GetPrayerTimetable", date, location, TimeZoneInfo.Local);
            Program.Assert(!ReferenceEquals(timetable, Call(controller, "GetPrayerTimetable", date,
                new PrayerTimesLocation("Other", 50.1, 4.2), TimeZoneInfo.Local)), "location change did not invalidate timetable");
            timetable = Call(controller, "GetPrayerTimetable", date, location, TimeZoneInfo.Local);
            Program.Assert(!ReferenceEquals(timetable, Call(controller, "GetPrayerTimetable", date, location,
                TimeZoneInfo.CreateCustomTimeZone("Synthetic offset", TimeSpan.FromHours(5), "Synthetic", "Synthetic"))),
                "timezone change did not invalidate timetable");

            Task? check = null;
            _ = Dispatcher.CurrentDispatcher.InvokeAsync(() => check = (Task)Call(controller, "CheckPrayerNotificationAsync"));
            PumpUntil(() => check is { IsCompleted: true });
            check!.GetAwaiter().GetResult();
            Program.Assert(!Field<DispatcherTimer>(controller, "_prayerNotificationTimer").IsEnabled,
                "disabled notifications kept a polling timer running");
            coordinator.NotifySettingsChanged();
            Program.Assert(Field<DispatcherTimer>(controller, "_prayerNotificationTimer").IsEnabled,
                "settings change did not restart notification checks");
            Console.WriteLine("Tray icon reuse, prayer cache invalidation, and disabled notification polling checks passed.");
        }
        finally { store.SaveAsync(original).GetAwaiter().GetResult(); }
    }

    private static SyncSettings? Load(TrayApplicationController controller)
    {
        var task = (Task<(SyncSettings? Settings, int Version)>)Call(controller, "GetPrayerSettingsAsync");
        PumpUntil(() => task.IsCompleted);
        return task.GetAwaiter().GetResult().Settings;
    }

    private static object Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void PumpUntil(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("Performance test operation did not complete.");
            var frame = new DispatcherFrame();
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
    }
}
