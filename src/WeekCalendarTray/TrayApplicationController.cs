using System.Windows.Threading;
using WeekCalendarTray.Core;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace WeekCalendarTray;

internal sealed class TrayApplicationController : IDisposable
{
    private readonly CalendarSyncCoordinator _syncCoordinator = new();
    private readonly PrayerTimesCalculator _prayerTimesCalculator = new();
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly DispatcherTimer _prayerNotificationTimer;
    private readonly DispatcherTimer _syncTimer;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private bool _prayerNotificationCheckRunning;
    private string? _lastPrayerNotificationKey;
    private DateOnly? _trayIconDate;
    private bool? _trayIconIsLightTheme;
    private SyncSettings? _cachedPrayerSettings;
    private int _prayerSettingsVersion;
    private DateOnly? _cachedPrayerTimetableDate;
    private PrayerTimesLocation? _cachedPrayerTimetableLocation;
    private TimeZoneInfo? _cachedPrayerTimetableTimeZone;
    private DailyPrayerTimes? _cachedPrayerTimetable;
    private MainWindow? _popup;

    public TrayApplicationController()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = BuildContextMenu(),
            Visible = false
        };
        _notifyIcon.MouseUp += NotifyIcon_MouseUp;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _timer.Tick += (_, _) => UpdateTrayStatus();

        _syncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(15)
        };
        _syncTimer.Tick += (_, _) => _ = AppDiagnostics.RunAsync("Background calendar sync", () => SyncNowAsync(showPopupStatus: false));

        _prayerNotificationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _prayerNotificationTimer.Tick += (_, _) => _ = AppDiagnostics.RunAsync("Prayer notification check", CheckPrayerNotificationAsync);
        _syncCoordinator.SettingsChanged += SyncCoordinator_SettingsChanged;
        ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
    }

    public void Start()
    {
        UpdateTrayStatus();
        _notifyIcon.Visible = true;
        _timer.Start();
        _syncTimer.Start();
        _prayerNotificationTimer.Start();
        _ = AppDiagnostics.RunAsync("Startup calendar sync", () => SyncNowAsync(showPopupStatus: false));
        _ = AppDiagnostics.RunAsync("Startup prayer notification", CheckPrayerNotificationAsync);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _timer.Stop();
        _syncTimer.Stop();
        _prayerNotificationTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
        _syncCoordinator.SettingsChanged -= SyncCoordinator_SettingsChanged;
        ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _popup?.Close();
        _syncCoordinator.Dispose();
        _lifetime.Dispose();
    }

    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RunOnUiThread(() => ShowPopup(resetToToday: false)));
        menu.Items.Add("Today", null, (_, _) => RunOnUiThread(() => ShowPopup(resetToToday: true)));
        menu.Items.Add("Sync now", null, (_, _) => RunOnUiThread(() => _ = AppDiagnostics.RunAsync("Manual calendar sync", () => SyncNowAsync(showPopupStatus: true))));
        menu.Items.Add("Settings...", null, (_, _) => RunOnUiThread(OpenSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => RunOnUiThread(() => WpfApplication.Current.Shutdown()));
        return menu;
    }

    private void NotifyIcon_MouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            RunOnUiThread(TogglePopup);
        }
    }

    private void TogglePopup()
    {
        if (_popup?.IsVisible == true)
        {
            _popup.Hide();
            return;
        }

        ShowPopup(resetToToday: false);
    }

    private void ShowPopup(bool resetToToday)
    {
        if (_disposed) return;
        if (_popup is null)
        {
            _popup = new MainWindow(_syncCoordinator);
            _popup.Closed += (_, _) => _popup = null;
        }

        if (resetToToday)
        {
            _popup.ShowToday();
        }

        PopupPositioner.PlaceNearTaskbar(_popup);
        ThemeManager.PrepareWindow(_popup);
        _popup.Show();
        _popup.Activate();
    }

    private async Task SyncNowAsync(bool showPopupStatus)
    {
        if (showPopupStatus)
        {
            ShowPopup(resetToToday: false);
        }

        if (!_disposed) await _syncCoordinator.SyncAsync(_lifetime.Token);
    }

    private void OpenSettings()
    {
        var existing = WpfApplication.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing is not null)
        {
            existing.Activate();
            return;
        }
        var settingsWindow = new SettingsWindow(_syncCoordinator);
        settingsWindow.Show();
        settingsWindow.Activate();
    }

    private void UpdateTrayStatus()
    {
        if (_disposed) return;
        var now = DateTime.Now;
        _notifyIcon.Text = TrayTextBuilder.Build(now);

        var today = DateOnly.FromDateTime(now);
        var isLightTheme = ThemeManager.IsLightTheme;
        if (_notifyIcon.Icon is null || _trayIconDate != today || _trayIconIsLightTheme != isLightTheme)
        {
            var previousIcon = _notifyIcon.Icon;
            _notifyIcon.Icon = TrayIconRenderer.Create(now);
            previousIcon?.Dispose();
            _trayIconDate = today;
            _trayIconIsLightTheme = isLightTheme;
        }
    }

    private async Task CheckPrayerNotificationAsync()
    {
        if (_disposed || _prayerNotificationCheckRunning)
        {
            return;
        }

        _prayerNotificationCheckRunning = true;
        try
        {
            var (settings, settingsVersion) = await GetPrayerSettingsAsync();
            if (_disposed || settings is null || settingsVersion != _prayerSettingsVersion)
            {
                return;
            }
            if (!settings.PrayerTimesEnabled || !settings.PrayerNotificationsEnabled)
            {
                _prayerNotificationTimer.Stop();
                return;
            }

            var location = new PrayerTimesLocation(
                settings.PrayerLocationName,
                settings.PrayerLatitude,
                settings.PrayerLongitude);
            var now = DateTimeOffset.Now;
            var today = DateOnly.FromDateTime(now.LocalDateTime);
            var timeZone = TimeZoneInfo.Local;
            var prayerTimes = GetPrayerTimetable(today, location, timeZone);

            if (_disposed || settingsVersion != _prayerSettingsVersion)
            {
                return;
            }

            foreach (var prayerTime in prayerTimes.Prayers.Where(IsNotificationPrayer))
            {
                var elapsed = now - prayerTime.Time;
                if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(90))
                {
                    continue;
                }

                var notificationKey = $"{today:yyyyMMdd}:{prayerTime.Name}:{location.Latitude:0.0000}:{location.Longitude:0.0000}";
                if (notificationKey == _lastPrayerNotificationKey)
                {
                    return;
                }

                _lastPrayerNotificationKey = notificationKey;
                ShowPrayerNotification(prayerTime, location);
                return;
            }
        }
        finally
        {
            _prayerNotificationCheckRunning = false;
        }
    }

    private async Task<(SyncSettings? Settings, int Version)> GetPrayerSettingsAsync()
    {
        if (_cachedPrayerSettings is not null)
        {
            return (_cachedPrayerSettings, _prayerSettingsVersion);
        }

        var loadVersion = _prayerSettingsVersion;
        var settings = await _syncCoordinator.SettingsStore.LoadAsync();
        if (_disposed || loadVersion != _prayerSettingsVersion)
        {
            return (null, loadVersion);
        }

        _cachedPrayerSettings = settings;
        return (settings, loadVersion);
    }

    private DailyPrayerTimes GetPrayerTimetable(
        DateOnly date,
        PrayerTimesLocation location,
        TimeZoneInfo timeZone)
    {
        if (_cachedPrayerTimetable is not null
            && _cachedPrayerTimetableDate == date
            && Equals(_cachedPrayerTimetableLocation, location)
            && Equals(_cachedPrayerTimetableTimeZone, timeZone))
        {
            return _cachedPrayerTimetable;
        }

        _cachedPrayerTimetable = _prayerTimesCalculator.Calculate(date, location, timeZone);
        _cachedPrayerTimetableDate = date;
        _cachedPrayerTimetableLocation = location;
        _cachedPrayerTimetableTimeZone = timeZone;
        return _cachedPrayerTimetable;
    }

    private void SyncCoordinator_SettingsChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (_disposed) return;
            _prayerSettingsVersion++;
            _cachedPrayerSettings = null;
            _cachedPrayerTimetable = null;
            _cachedPrayerTimetableDate = null;
            _cachedPrayerTimetableLocation = null;
            _cachedPrayerTimetableTimeZone = null;
            _prayerNotificationTimer.Start();
        });
    }

    private void ShowPrayerNotification(PrayerTime prayerTime, PrayerTimesLocation location)
    {
        if (_disposed) return;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.BalloonTipTitle = $"{prayerTime.Name} prayer time";
        _notifyIcon.BalloonTipText = $"{prayerTime.Name} is now at {prayerTime.Time.LocalDateTime:HH:mm} for {location.Name}.";
        _notifyIcon.ShowBalloonTip(10000);
    }

    private static bool IsNotificationPrayer(PrayerTime prayerTime)
    {
        return !prayerTime.Name.Equals("Shuruq", StringComparison.OrdinalIgnoreCase);
    }

    private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(UpdateTrayStatus);
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = WpfApplication.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
