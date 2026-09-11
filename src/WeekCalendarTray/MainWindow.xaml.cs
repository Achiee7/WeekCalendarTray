using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WeekCalendarTray.Core;
using Controls = System.Windows.Controls;
using Primitives = System.Windows.Controls.Primitives;

namespace WeekCalendarTray;

internal enum CalendarDisplayMode
{
    Month,
    Year,
    Decade,
    Day
}

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double BaseWindowWidth = 372d;
    private const double PrayerToggleWidth = 28d;
    private const double PrayerPanelWidth = 226d;

    private readonly CalendarSyncCoordinator _syncCoordinator;
    private readonly PrayerTimesCalculator _prayerTimesCalculator = new();
    private readonly DispatcherTimer _prayerTimer;
    private CalendarDisplayMode _displayMode = CalendarDisplayMode.Month;
    private DateOnly _selectedDate;
    private DateOnly _today;
    private DateOnly _visibleMonth;
    private int _eventRefreshVersion;
    private bool _keepOpenForChildWindow;
    private EventDetailsWindow? _detailsWindow;
    private Controls.ToolTip? _activeDayPreview;
    private bool _closingChildren;
    private bool _closed;
    private (DateOnly Date, PrayerTimesLocation Location, DateTimeOffset Next)? _displayedPrayerState;
    private bool _prayerTimesEnabled;
    private bool _prayerPanelExpanded;
    private string _syncStatusText = "Sync not configured.";
    private string _prayerCountdownText = "Prayer times off";
    private string _prayerLocationText = string.Empty;
    private string _prayerDateText = string.Empty;
    private PrayerTimesLocation _prayerLocation = new("Leerdam, NL", 51.8936d, 5.0913d);

    internal MainWindow(CalendarSyncCoordinator syncCoordinator)
    {
        _syncCoordinator = syncCoordinator;
        _today = DateOnly.FromDateTime(DateTime.Now);
        _selectedDate = _today;
        _visibleMonth = CalendarGridBuilder.FirstDayOfMonth(_today);

        PreviousMonthCommand = new RelayCommand(_ => MoveMonth(-1));
        NextMonthCommand = new RelayCommand(_ => MoveMonth(1));
        TodayCommand = new RelayCommand(_ => ShowToday());
        HideCommand = new RelayCommand(_ => Hide());
        SelectDateCommand = new RelayCommand(SelectDate);
        ZoomOutCalendarCommand = new RelayCommand(_ => ZoomOutCalendar());
        SelectPeriodCommand = new RelayCommand(SelectPeriod);
        AddEventCommand = new RelayCommand(_ => _ = RunUiOperationAsync(OpenAddEventAsync));
        SyncNowCommand = new RelayCommand(_ => _ = RunUiOperationAsync(SyncNowAsync));
        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        OpenEventDetailsCommand = new RelayCommand(OpenEventDetails);
        TogglePrayerPanelCommand = new RelayCommand(_ => TogglePrayerPanel());
        ShowMonthViewCommand = new RelayCommand(_ => ShowMonthView());
        ShowDayViewCommand = new RelayCommand(_ => ShowDayView());
        DayOrTodayCommand = new RelayCommand(_ =>
        {
            if (_displayMode == CalendarDisplayMode.Day) ShowToday();
            else ShowDayView();
        });

        _prayerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _prayerTimer.Tick += (_, _) => RefreshPrayerPanel();

        InitializeComponent();
        DataContext = this;
        _syncCoordinator.EventsChanged += SyncCoordinator_EventsChanged;
        _syncCoordinator.SettingsChanged += SyncCoordinator_SettingsChanged;
        RefreshCalendar();
        _ = RunUiOperationAsync(LoadPrayerSettingsAsync);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                CloseDayPreview();
                CloseTransientWindows();
            }
            if (IsVisible && _prayerTimesEnabled)
            {
                RefreshPrayerPanel();
                _prayerTimer.Start();
            }
            else _prayerTimer.Stop();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CalendarWeekViewModel> Weeks { get; } = [];

    public ObservableCollection<CalendarPeriodButtonViewModel> OverviewItems { get; } = [];

    public ObservableCollection<CalendarEventViewModel> SelectedDayEvents { get; } = [];

    public ObservableCollection<PrayerTimeViewModel> PrayerTimes { get; } = [];

    public ICommand PreviousMonthCommand { get; }

    public ICommand NextMonthCommand { get; }

    public ICommand ShowMonthViewCommand { get; }

    public ICommand DayOrTodayCommand { get; }

    public ICommand ShowDayViewCommand { get; }

    public ICommand TodayCommand { get; }

    public ICommand HideCommand { get; }

    public ICommand SelectDateCommand { get; }

    public ICommand ZoomOutCalendarCommand { get; }

    public ICommand SelectPeriodCommand { get; }

    public ICommand AddEventCommand { get; }

    public ICommand SyncNowCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenEventDetailsCommand { get; }

    public ICommand TogglePrayerPanelCommand { get; }

    public string SelectedDateTitle => _selectedDate
        .ToDateTime(TimeOnly.MinValue)
        .ToString("dddd d MMMM", CultureInfo.CurrentCulture);

    public string SelectedWeekTitle => $"Week {CalendarGridBuilder.GetIsoWeekNumber(_selectedDate):00}";

    public string MonthTitle => _displayMode switch
    {
        CalendarDisplayMode.Day => _selectedDate.ToDateTime(TimeOnly.MinValue).ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture),
        CalendarDisplayMode.Year => _visibleMonth.Year.ToString(CultureInfo.CurrentCulture),
        CalendarDisplayMode.Decade => $"{GetDecadeStart(_visibleMonth.Year)} - {GetDecadeStart(_visibleMonth.Year) + 9}",
        _ => _visibleMonth.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", CultureInfo.CurrentCulture)
    };

    public Visibility MonthViewVisibility => _displayMode == CalendarDisplayMode.Month
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility OverviewVisibility => _displayMode is CalendarDisplayMode.Year or CalendarDisplayMode.Decade
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DayViewVisibility => _displayMode == CalendarDisplayMode.Day
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool IsMonthView => _displayMode == CalendarDisplayMode.Month;

    public bool IsDayView => _displayMode == CalendarDisplayMode.Day;

    public GridLength CalendarGridRowHeight => _displayMode == CalendarDisplayMode.Day
        ? new GridLength(0)
        : new GridLength(264);

    public DateOnly SelectedDate => _selectedDate;

    public GridLength WeekdayHeaderRowHeight => _displayMode == CalendarDisplayMode.Day
        ? new GridLength(0)
        : new GridLength(30);

    // Day view already names the date in the header, so its separate title row would
    // only repeat it.
    public GridLength NavigationTitleRowHeight => _displayMode == CalendarDisplayMode.Day
        ? new GridLength(0)
        : new GridLength(40);

    public Visibility NavigationTitleVisibility => _displayMode == CalendarDisplayMode.Day
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string DayButtonContent => _displayMode == CalendarDisplayMode.Day ? "Today" : "Day";

    public string DayButtonToolTip => _displayMode == CalendarDisplayMode.Day
        ? "Jump to today"
        : "Show the full agenda for the selected day";

    public double AgendaMaxHeight => _displayMode == CalendarDisplayMode.Day ? 390 : 104;

    public string PreviousStepToolTip => _displayMode switch
    {
        CalendarDisplayMode.Day => "Previous day",
        CalendarDisplayMode.Year => "Previous year",
        CalendarDisplayMode.Decade => "Previous decade",
        _ => "Previous month"
    };

    public string NextStepToolTip => _displayMode switch
    {
        CalendarDisplayMode.Day => "Next day",
        CalendarDisplayMode.Year => "Next year",
        CalendarDisplayMode.Decade => "Next decade",
        _ => "Next month"
    };

    public GridLength PrayerToggleColumnWidth => _prayerTimesEnabled
        ? new GridLength(PrayerToggleWidth)
        : new GridLength(0);

    public GridLength PrayerPanelColumnWidth => _prayerTimesEnabled && _prayerPanelExpanded
        ? new GridLength(PrayerPanelWidth)
        : new GridLength(0);

    public Visibility PrayerFeatureVisibility => _prayerTimesEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PrayerPanelVisibility => _prayerTimesEnabled && _prayerPanelExpanded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string PrayerToggleContent => _prayerPanelExpanded ? ">" : "<";

    public string PrayerToggleToolTip => _prayerPanelExpanded
        ? "Hide prayer times"
        : "Show prayer times";

    public string AgendaTitle => $"Agenda for {_selectedDate.ToDateTime(TimeOnly.MinValue).ToString("ddd d MMM", CultureInfo.CurrentCulture)}";

    public string AgendaEmptyText => SelectedDayEvents.Count == 0
        ? "No events found for this day."
        : string.Empty;

    public Visibility AgendaEmptyVisibility => SelectedDayEvents.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string PrayerCountdownText
    {
        get => _prayerCountdownText;
        private set
        {
            if (_prayerCountdownText == value)
            {
                return;
            }

            _prayerCountdownText = value;
            OnPropertyChanged();
        }
    }

    public string PrayerLocationText
    {
        get => _prayerLocationText;
        private set
        {
            if (_prayerLocationText == value)
            {
                return;
            }

            _prayerLocationText = value;
            OnPropertyChanged();
        }
    }

    public string PrayerDateText
    {
        get => _prayerDateText;
        private set
        {
            if (_prayerDateText == value)
            {
                return;
            }

            _prayerDateText = value;
            OnPropertyChanged();
        }
    }

    public string SyncStatusText
    {
        get => _syncStatusText;
        private set
        {
            if (_syncStatusText == value)
            {
                return;
            }

            _syncStatusText = value;
            OnPropertyChanged();
        }
    }

    public void ShowToday()
    {
        _today = DateOnly.FromDateTime(DateTime.Now);
        _selectedDate = _today;
        _visibleMonth = CalendarGridBuilder.FirstDayOfMonth(_today);
        _displayMode = CalendarDisplayMode.Day;
        RefreshCalendar();
    }

    private void ShowMonthView()
    {
        _visibleMonth = CalendarGridBuilder.FirstDayOfMonth(_selectedDate);
        _displayMode = CalendarDisplayMode.Month;
        RefreshCalendar();
    }

    private void ShowDayView()
    {
        _displayMode = CalendarDisplayMode.Day;
        RefreshCalendar();
    }

    private async Task LoadPrayerSettingsAsync()
    {
        var settings = await _syncCoordinator.SettingsStore.LoadAsync();
        if (_closed) return;
        _prayerTimesEnabled = settings.PrayerTimesEnabled;
        _prayerLocation = new PrayerTimesLocation(
            settings.PrayerLocationName,
            settings.PrayerLatitude,
            settings.PrayerLongitude);

        if (!_prayerTimesEnabled)
        {
            _prayerPanelExpanded = false;
        }

        UpdatePrayerLayout(reposition: IsVisible);
        if (IsVisible && _prayerTimesEnabled) _prayerTimer.Start();
        else _prayerTimer.Stop();
        RefreshPrayerPanel();
    }

    private void TogglePrayerPanel()
    {
        if (_closed) return;
        if (!_prayerTimesEnabled)
        {
            _displayedPrayerState = null;
            return;
        }

        _prayerPanelExpanded = !_prayerPanelExpanded;
        UpdatePrayerLayout(reposition: IsVisible);
        RefreshPrayerPanel();
    }

    private void UpdatePrayerLayout(bool reposition)
    {
        var oldWidth = Width;
        Width = BaseWindowWidth
            + (_prayerTimesEnabled ? PrayerToggleWidth : 0)
            + (_prayerTimesEnabled && _prayerPanelExpanded ? PrayerPanelWidth : 0);

        if (reposition)
        {
            var delta = Width - oldWidth;
            Left = Clamp(Left - delta, SystemParameters.WorkArea.Left + 8, SystemParameters.WorkArea.Right - Width - 8);
        }

        OnPropertyChanged(nameof(PrayerToggleColumnWidth));
        OnPropertyChanged(nameof(PrayerPanelColumnWidth));
        OnPropertyChanged(nameof(PrayerFeatureVisibility));
        OnPropertyChanged(nameof(PrayerPanelVisibility));
        OnPropertyChanged(nameof(PrayerToggleContent));
        OnPropertyChanged(nameof(PrayerToggleToolTip));
    }

    private void RefreshPrayerPanel()
    {
        if (_closed) return;
        if (!_prayerTimesEnabled)
        {
            _displayedPrayerState = null;
            PrayerTimes.Clear();
            PrayerCountdownText = "Prayer times off";
            PrayerLocationText = string.Empty;
            PrayerDateText = string.Empty;
            return;
        }

        var timeZone = TimeZoneInfo.Local;
        var now = DateTimeOffset.Now;
        var nextPrayer = GetNextPrayer(now, timeZone);
        var selectedDateIsNextPrayerDate = DateOnly.FromDateTime(nextPrayer.Time.LocalDateTime) == _selectedDate;
        var state = (_selectedDate, _prayerLocation, nextPrayer.Time);
        if (_displayedPrayerState != state)
        {
            var selectedPrayerTimes = _prayerTimesCalculator.Calculate(_selectedDate, _prayerLocation, timeZone);
            PrayerTimes.Clear();
            foreach (var prayerTime in selectedPrayerTimes.Prayers)
            {
                var isNext = selectedDateIsNextPrayerDate
                    && IsCountdownPrayer(prayerTime)
                    && prayerTime.Name.Equals(nextPrayer.Name, StringComparison.OrdinalIgnoreCase);
                PrayerTimes.Add(new PrayerTimeViewModel(prayerTime, isNext));
            }
            _displayedPrayerState = state;
        }

        var remaining = nextPrayer.Time - now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        PrayerCountdownText = $"{nextPrayer.Name.ToUpperInvariant()} {FormatCountdown(remaining)}";
        PrayerLocationText = _prayerLocation.Name;
        PrayerDateText = _selectedDate
            .ToDateTime(TimeOnly.MinValue)
            .ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture);
    }

    private PrayerTime GetNextPrayer(DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        foreach (var date in new[] { today, today.AddDays(1) })
        {
            var dayPrayerTimes = _prayerTimesCalculator.Calculate(date, _prayerLocation, timeZone);
            var nextPrayer = dayPrayerTimes.Prayers
                .Where(IsCountdownPrayer)
                .FirstOrDefault(prayerTime => prayerTime.Time > now);

            if (nextPrayer is not null)
            {
                return nextPrayer;
            }
        }

        return _prayerTimesCalculator
            .Calculate(today.AddDays(1), _prayerLocation, timeZone)
            .Prayers
            .First(IsCountdownPrayer);
    }

    private static bool IsCountdownPrayer(PrayerTime prayerTime)
    {
        return !prayerTime.Name.Equals("Shuruq", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        return $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Min(Math.Max(value, min), max);
    }

    private void MoveMonth(int monthOffset)
    {
        if (_displayMode == CalendarDisplayMode.Day)
        {
            var dayIndex = _selectedDate.DayNumber + monthOffset;
            if (dayIndex < DateOnly.MinValue.DayNumber || dayIndex > DateOnly.MaxValue.DayNumber)
            {
                return;
            }

            _selectedDate = DateOnly.FromDayNumber(dayIndex);
            _visibleMonth = CalendarGridBuilder.FirstDayOfMonth(_selectedDate);
            RefreshCalendar();
            return;
        }

        var months = monthOffset * (_displayMode == CalendarDisplayMode.Decade ? 120
            : _displayMode == CalendarDisplayMode.Year ? 12 : 1);
        var monthIndex = (_visibleMonth.Year - 1) * 12 + _visibleMonth.Month - 1 + months;
        if (monthIndex < 12 || monthIndex >= 9998 * 12) return;
        if (_displayMode == CalendarDisplayMode.Decade)
        {
            _visibleMonth = _visibleMonth.AddYears(monthOffset * 10);
        }
        else if (_displayMode == CalendarDisplayMode.Year)
        {
            _visibleMonth = _visibleMonth.AddYears(monthOffset);
        }
        else
        {
            _visibleMonth = _visibleMonth.AddMonths(monthOffset);
            var selectedDay = Math.Min(_selectedDate.Day, DateTime.DaysInMonth(_visibleMonth.Year, _visibleMonth.Month));
            _selectedDate = new DateOnly(_visibleMonth.Year, _visibleMonth.Month, selectedDay);
        }

        RefreshCalendar();
    }

    private void SelectDate(object? parameter)
    {
        if (parameter is not CalendarDayViewModel day || day.Date.Year is < 2 or > 9998)
        {
            return;
        }

        _selectedDate = day.Date;
        _visibleMonth = CalendarGridBuilder.FirstDayOfMonth(day.Date);
        _displayMode = CalendarDisplayMode.Month;
        RefreshCalendar();
    }

    private void ZoomOutCalendar()
    {
        _displayMode = _displayMode switch
        {
            CalendarDisplayMode.Day => CalendarDisplayMode.Month,
            CalendarDisplayMode.Month => CalendarDisplayMode.Year,
            CalendarDisplayMode.Year => CalendarDisplayMode.Decade,
            _ => CalendarDisplayMode.Decade
        };

        RefreshCalendar();
    }

    private void SelectPeriod(object? parameter)
    {
        if (parameter is not CalendarPeriodButtonViewModel period)
        {
            return;
        }

        if (_displayMode == CalendarDisplayMode.Decade)
        {
            if (period.Value is < 2 or > 9998) return;
            _visibleMonth = new DateOnly(period.Value, _visibleMonth.Month, 1);
            _displayMode = CalendarDisplayMode.Year;
            RefreshCalendar();
            return;
        }

        if (_displayMode != CalendarDisplayMode.Year)
        {
            return;
        }

        var year = _visibleMonth.Year;
        var month = period.Value;
        var selectedDay = Math.Min(_selectedDate.Day, DateTime.DaysInMonth(year, month));
        _visibleMonth = new DateOnly(year, month, 1);
        _selectedDate = new DateOnly(year, month, selectedDay);
        _displayMode = CalendarDisplayMode.Month;
        RefreshCalendar();
    }

    private void RefreshCalendar()
    {
        CloseDayPreview();
        CloseEventDetails();
        _today = DateOnly.FromDateTime(DateTime.Now);
        var grid = CalendarGridBuilder.CreateMonth(_visibleMonth, _selectedDate, _today);

        Weeks.Clear();
        foreach (var week in grid.Weeks)
        {
            var days = week.Days
                .Select(day => new CalendarDayViewModel(day))
                .ToList();
            Weeks.Add(new CalendarWeekViewModel(week.IsoWeekNumber, days));
        }

        RefreshOverviewItems();

        OnPropertyChanged(nameof(SelectedDateTitle));
        OnPropertyChanged(nameof(SelectedWeekTitle));
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(MonthViewVisibility));
        OnPropertyChanged(nameof(OverviewVisibility));
        OnPropertyChanged(nameof(DayViewVisibility));
        OnPropertyChanged(nameof(IsMonthView));
        OnPropertyChanged(nameof(IsDayView));
        OnPropertyChanged(nameof(CalendarGridRowHeight));
        OnPropertyChanged(nameof(WeekdayHeaderRowHeight));
        OnPropertyChanged(nameof(NavigationTitleRowHeight));
        OnPropertyChanged(nameof(NavigationTitleVisibility));
        OnPropertyChanged(nameof(DayButtonContent));
        OnPropertyChanged(nameof(DayButtonToolTip));
        OnPropertyChanged(nameof(AgendaMaxHeight));
        OnPropertyChanged(nameof(PreviousStepToolTip));
        OnPropertyChanged(nameof(NextStepToolTip));
        OnPropertyChanged(nameof(AgendaTitle));
        OnPropertyChanged(nameof(AgendaEmptyText));
        OnPropertyChanged(nameof(AgendaEmptyVisibility));
        RefreshPrayerPanel();
        _ = RunUiOperationAsync(RefreshEventsFromCacheAsync);
    }

    private void RefreshOverviewItems()
    {
        OverviewItems.Clear();

        if (_displayMode == CalendarDisplayMode.Year)
        {
            for (var month = 1; month <= 12; month++)
            {
                var label = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month);
                OverviewItems.Add(new CalendarPeriodButtonViewModel(
                    label,
                    month,
                    _visibleMonth.Year == _today.Year && month == _today.Month,
                    _visibleMonth.Year == _selectedDate.Year && month == _selectedDate.Month));
            }

            return;
        }

        if (_displayMode != CalendarDisplayMode.Decade)
        {
            return;
        }

        var decadeStart = GetDecadeStart(_visibleMonth.Year);
        for (var year = Math.Max(2, decadeStart); year < Math.Min(9999, decadeStart + 10); year++)
        {
            OverviewItems.Add(new CalendarPeriodButtonViewModel(
                year.ToString(CultureInfo.CurrentCulture),
                year,
                year == _today.Year,
                year == _selectedDate.Year));
        }
    }

    private async Task RefreshEventsFromCacheAsync()
    {
        var refreshVersion = ++_eventRefreshVersion;
        var selectedDate = _selectedDate;
        var visibleDays = Weeks
            .SelectMany(week => week.Days)
            .ToList();

        var settingsTask = _syncCoordinator.SettingsStore.LoadAsync();
        var localStoreTask = LocalCalendarEventStore.LoadAsync();
        var cacheTask = CalendarEventCache.LoadAsync();
        await Task.WhenAll(settingsTask, localStoreTask, cacheTask);
        var cache = await cacheTask;
        var settings = await settingsTask;
        var localStore = await localStoreTask;
        var sourceNames = settings.Subscriptions
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.Name))
            .ToDictionary(subscription => subscription.Id, subscription => subscription.Name, StringComparer.OrdinalIgnoreCase);
        var eventsByDate = visibleDays
            .Select(day => day.Date)
            .Distinct()
            .ToDictionary(date => date, date => MergeEvents(
                ApplyCalendarNames(cache.GetEventsFor(date), sourceNames),
                localStore.GetEventsFor(date)));

        var selectedEvents = eventsByDate.TryGetValue(selectedDate, out var cachedSelectedEvents)
            ? cachedSelectedEvents
            : MergeEvents(ApplyCalendarNames(cache.GetEventsFor(selectedDate), sourceNames), localStore.GetEventsFor(selectedDate));

        await Dispatcher.InvokeAsync(() =>
        {
            if (_closed || refreshVersion != _eventRefreshVersion)
            {
                return;
            }

            foreach (var day in visibleDays)
            {
                day.UpdateEvents(
                    eventsByDate[day.Date],
                    settings.ShowEventIndicators,
                    settings.ShowEventPreviewOnHover);
            }

            SelectedDayEvents.Clear();
            foreach (var calendarEvent in selectedEvents)
            {
                SelectedDayEvents.Add(new CalendarEventViewModel(calendarEvent));
            }

            OnPropertyChanged(nameof(AgendaEmptyText));
            OnPropertyChanged(nameof(AgendaEmptyVisibility));
        });
    }

    private async Task SyncNowAsync()
    {
        SyncStatusText = "Syncing calendars...";
        var result = await _syncCoordinator.SyncAsync();
        SyncStatusText = result.Summary;
        await RefreshEventsFromCacheAsync();
    }

    private async Task OpenAddEventAsync()
    {
        CloseDayPreview();
        CloseEventDetails();
        _keepOpenForChildWindow = true;
        var addEventWindow = new AddEventWindow(_selectedDate)
        {
            Owner = this
        };

        bool wasSaved;
        try { wasSaved = addEventWindow.ShowDialog() == true; }
        finally { _keepOpenForChildWindow = false; }

        if (wasSaved && addEventWindow.CreatedEvent is { } localEvent)
        {
            var store = await LocalCalendarEventStore.LoadAsync();
            store.Add(localEvent);
            await store.SaveAsync();
            SyncStatusText = "Local event added.";
            await RefreshEventsFromCacheAsync();
        }

        if (IsVisible)
        {
            Activate();
        }
    }

    private void OpenSettings()
    {
        CloseDayPreview();
        CloseEventDetails();
        var existing = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing is not null)
        {
            existing.Activate();
            return;
        }
        var settingsWindow = new SettingsWindow(_syncCoordinator)
        {
            Owner = this
        };
        settingsWindow.Show();
        settingsWindow.Activate();
    }

    private void OpenEventDetails(object? parameter)
    {
        if (_closed || !IsVisible || parameter is not CalendarEventViewModel calendarEvent)
        {
            return;
        }

        CloseDayPreview();
        if (_detailsWindow is not null)
        {
            _detailsWindow.UpdateEvent(calendarEvent);
            _detailsWindow.Activate();
            return;
        }
        var detailsWindow = new EventDetailsWindow(calendarEvent)
        {
            Owner = this
        };
        _detailsWindow = detailsWindow;
        detailsWindow.DeleteRequested += (_, eventToDelete) => _ = RunUiOperationAsync(() => DeleteLocalEventAsync(eventToDelete));

        PositionDetailsWindow(detailsWindow);
        detailsWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_detailsWindow, detailsWindow)) _detailsWindow = null;
            if (!_closed && !_closingChildren && IsVisible)
            {
                Activate();
            }
        };

        detailsWindow.Show();
        detailsWindow.Activate();
    }

    private void CloseEventDetails()
    {
        _detailsWindow?.Close();
    }

    private void CloseTransientWindows()
    {
        if (_closingChildren) return;
        _closingChildren = true;
        try
        {
            foreach (var child in OwnedWindows.OfType<Window>().ToArray())
                child.Close();
        }
        finally { _closingChildren = false; }
    }

    private void CloseDayPreview()
    {
        if (_activeDayPreview is not null) _activeDayPreview.IsOpen = false;
        _activeDayPreview = null;
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) => CloseDayPreview();

    private void DayPreview_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => CloseDayPreview();

    private void DayPreview_Opening(object sender, Controls.ToolTipEventArgs e)
    {
        if (sender is not Controls.Button button
            || button.DataContext is not CalendarDayViewModel day
            || string.IsNullOrWhiteSpace(day.EventPreviewText)
            || OwnedWindows.OfType<Window>().Any(child => child.IsVisible)
            || !IsVisible)
        {
            e.Handled = true;
            return;
        }

        var workArea = PopupPositioner.GetWorkArea(this);
        const double previewWidth = 280;
        var fitsLeft = Left - previewWidth - 8 >= workArea.Left;
        var fitsRight = Left + ActualWidth + previewWidth + 8 <= workArea.Right;
        if (!fitsLeft && !fitsRight)
        {
            e.Handled = true;
            return;
        }
        CloseDayPreview();
        var preview = (Controls.ToolTip)button.ToolTip;
        preview.DataContext = day;
        preview.PlacementTarget = this;
        preview.Placement = Primitives.PlacementMode.Custom;
        var rowY = button.TranslatePoint(new System.Windows.Point(0, 0), this).Y;
        preview.CustomPopupPlacementCallback = (size, target, offset) =>
        [
            new Primitives.CustomPopupPlacement(
                new System.Windows.Point(fitsLeft ? -size.Width - 8 : target.Width + 8,
                    Math.Clamp(rowY, 0, Math.Max(0, ActualHeight - size.Height))),
                Primitives.PopupPrimaryAxis.Vertical)
        ];
        _activeDayPreview = preview;
    }

    private async Task DeleteLocalEventAsync(CalendarEventViewModel calendarEvent)
    {
        if (!calendarEvent.IsLocalEvent)
        {
            return;
        }

        var store = await LocalCalendarEventStore.LoadAsync();
        if (!store.Remove(calendarEvent.Id))
        {
            return;
        }

        await store.SaveAsync();
        SyncStatusText = "Local event deleted.";
        await RefreshEventsFromCacheAsync();
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        MoreMenuPopup.IsOpen = !MoreMenuPopup.IsOpen;
    }

    private void SyncNowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MoreMenuPopup.IsOpen = false;
        _ = RunUiOperationAsync(SyncNowAsync);
    }

    private void OpenSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MoreMenuPopup.IsOpen = false;
        OpenSettings();
    }

    private static IReadOnlyList<CalendarEvent> MergeEvents(
        IReadOnlyList<CalendarEvent> syncedEvents,
        IReadOnlyList<CalendarEvent> localEvents)
    {
        return syncedEvents
            .Concat(localEvents)
            .OrderBy(calendarEvent => calendarEvent.IsAllDay ? 0 : 1)
            .ThenBy(calendarEvent => calendarEvent.Start)
            .ThenBy(calendarEvent => calendarEvent.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<CalendarEvent> ApplyCalendarNames(
        IReadOnlyList<CalendarEvent> events,
        IReadOnlyDictionary<string, string> sourceNames)
    {
        return events
            .Select(calendarEvent => sourceNames.TryGetValue(calendarEvent.SourceId, out var sourceName)
                ? calendarEvent with { SourceName = sourceName }
                : calendarEvent)
            .ToList();
    }

    private static int GetDecadeStart(int year)
    {
        return year - (year % 10);
    }

    private void PositionDetailsWindow(Window detailsWindow)
    {
        var workArea = PopupPositioner.GetWorkArea(this);
        detailsWindow.MaxHeight = Math.Max(200, workArea.Height - 16);
        detailsWindow.Height = Math.Min(detailsWindow.Height, detailsWindow.MaxHeight);
        var left = Left - detailsWindow.Width - 10;
        if (left < workArea.Left)
        {
            left = Left + Width + 10;
        }

        if (left + detailsWindow.Width > workArea.Right)
        {
            left = workArea.Right - detailsWindow.Width - 8;
        }

        var top = Math.Min(
            Math.Max(Top, workArea.Top + 8),
            workArea.Bottom - detailsWindow.Height - 8);

        detailsWindow.Left = left;
        detailsWindow.Top = top;
    }

    private void SyncCoordinator_EventsChanged(object? sender, EventArgs e)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.InvokeAsync(() => _ = RunUiOperationAsync(RefreshEventsFromCacheAsync));
    }

    private void SyncCoordinator_SettingsChanged(object? sender, EventArgs e)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.InvokeAsync(() => _ = RunUiOperationAsync(async () =>
        {
            await LoadPrayerSettingsAsync();
            await RefreshEventsFromCacheAsync();
        }));
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_closed) return;
            var hasVisibleChildWindow = OwnedWindows.OfType<Window>().Any(window => window.IsVisible);
            if (!IsActive && !_keepOpenForChildWindow && !hasVisibleChildWindow)
            {
                Hide();
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        CloseDayPreview();
        CloseTransientWindows();
        ++_eventRefreshVersion;
        _prayerTimer.Stop();
        _syncCoordinator.EventsChanged -= SyncCoordinator_EventsChanged;
        _syncCoordinator.SettingsChanged -= SyncCoordinator_SettingsChanged;
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        CloseDayPreview();
        CloseTransientWindows();
        base.OnClosing(e);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private Task RunUiOperationAsync(Func<Task> action)
    {
        // The window is constructed before the dispatcher loop runs, so at that point
        // there is no DispatcherSynchronizationContext to capture. Continuations after
        // the first await would then resume on a thread-pool thread and throw on any
        // UI access. Queue the operation so it starts inside a dispatcher callback,
        // where WPF installs that context and awaits resume on the UI thread.
        if (SynchronizationContext.Current is not DispatcherSynchronizationContext)
        {
            return Dispatcher.InvokeAsync(() => RunUiOperationAsync(action)).Task.Unwrap();
        }

        return AppDiagnostics.RunAsync("Calendar operation", action, () =>
        {
            if (!_closed) SyncStatusText = "Could not complete this operation. Please try again.";
        });
    }
}
