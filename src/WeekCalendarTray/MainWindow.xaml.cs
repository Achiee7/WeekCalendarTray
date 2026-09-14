using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WeekCalendarTray.Core;
using Controls = System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using Primitives = System.Windows.Controls.Primitives;

namespace WeekCalendarTray;

internal enum CalendarDisplayMode
{
    Month,
    Week,
    Year,
    Decade
}

internal enum SidePaneMode
{
    Day,
    Prayer,
    Details
}

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double SideToggleWidth = 28d;
    private const double SidePanelWidth = 320d;

    // Calendar body width excludes the side-pane chrome. It is seeded from settings,
    // updated as the user drags, and persisted independently of the pane state.
    private double _baseWindowWidth = PopupSize.DefaultWidth;
    private double _appliedSideChromeWidth;
    private double _persistedPopupWidth = PopupSize.DefaultWidth;
    private double _persistedPopupHeight = PopupSize.DefaultHeight;
    private bool _applyingLayoutWidth;
    private DispatcherTimer? _popupSizeSaveTimer;

    private readonly CalendarSyncCoordinator _syncCoordinator;
    private readonly PrayerTimesCalculator _prayerTimesCalculator = new();
    private readonly Dictionary<DateOnly, DailyPrayerTimes> _prayerCache = [];
    private TimeZoneInfo? _prayerCacheZone;
    private PrayerTimesLocation? _prayerCacheLocation;
    private readonly DispatcherTimer _uiTimer;
    private CalendarDisplayMode _displayMode = CalendarDisplayMode.Month;
    private DateOnly _selectedDate;
    private DateOnly _today;
    private DateOnly _visibleMonth;
    private int _eventRefreshVersion;
    private bool _keepOpenForChildWindow;
    private Controls.ToolTip? _activeDayPreview;
    private bool _closingChildren;
    private bool _closed;
    private (DateOnly Date, PrayerTimesLocation Location, DateTimeOffset Next)? _displayedPrayerState;
    private bool _prayerTimesEnabled;
    private bool _sidePanelExpanded;
    private SidePaneMode _sidePaneMode = SidePaneMode.Day;
    private CalendarEventViewModel? _selectedEvent;
    private string _dayViewLayout = "List";
    private IReadOnlyList<CalendarEventViewModel> _dayTimelineEvents = [];
    private IReadOnlyList<CalendarEventViewModel> _weekTimelineEvents = [];
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
        ToggleSidePanelCommand = new RelayCommand(_ => ToggleSidePanel());
        ShowDayPaneCommand = new RelayCommand(_ => ShowDayPane());
        ShowPrayerPaneCommand = new RelayCommand(_ => ShowPrayerPane());
        BackFromDetailsCommand = new RelayCommand(_ => ShowDayPane());
        ShowMonthViewCommand = new RelayCommand(_ => ShowMonthView());
        ShowWeekViewCommand = new RelayCommand(_ => ShowWeekView());

        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _uiTimer.Tick += (_, _) =>
        {
            var currentDate = DateOnly.FromDateTime(DateTime.Now);
            if (currentDate != _today)
            {
                RefreshCalendar();
            }
            if (WeekTimeline.IsVisible) WeekTimeline.RefreshCurrentTime();
            if (DayTimeline.IsVisible) DayTimeline.RefreshCurrentTime();
            if (_prayerTimesEnabled && _sidePanelExpanded && IsPrayerPaneActive) RefreshPrayerPanel();
        };

        InitializeComponent();
        DataContext = this;
        DayTimeline.EventSelected += (_, calendarEvent) => OpenEventDetails(calendarEvent);
        DayTimeline.DateSelected += (_, date) => SelectTimelineDate(date);
        WeekTimeline.EventSelected += (_, calendarEvent) =>
        {
            SelectTimelineDate(WeekTimeline.SelectedEventDate);
            OpenEventDetails(calendarEvent);
        };
        WeekTimeline.DateSelected += (_, date) => SelectTimelineDate(date);
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
                FlushPendingPopupSizeSave();
            }
            if (IsVisible)
            {
                RefreshTimelineViews();
                if (_prayerTimesEnabled) RefreshPrayerPanel();
                _uiTimer.Start();
            }
            else _uiTimer.Stop();
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

    public ICommand ShowWeekViewCommand { get; }

    public ICommand TodayCommand { get; }

    public ICommand HideCommand { get; }

    public ICommand SelectDateCommand { get; }

    public ICommand ZoomOutCalendarCommand { get; }

    public ICommand SelectPeriodCommand { get; }

    public ICommand AddEventCommand { get; }

    public ICommand SyncNowCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenEventDetailsCommand { get; }

    public ICommand ToggleSidePanelCommand { get; }

    public ICommand ShowDayPaneCommand { get; }

    public ICommand ShowPrayerPaneCommand { get; }

    public ICommand BackFromDetailsCommand { get; }

    public string SelectedDateTitle => _selectedDate
        .ToDateTime(TimeOnly.MinValue)
        .ToString("dddd d MMMM", CultureInfo.CurrentCulture);

    public string SelectedWeekTitle => $"Week {CalendarGridBuilder.GetIsoWeekNumber(_selectedDate):00}";

    public string MonthTitle => _displayMode switch
    {
        CalendarDisplayMode.Week => GetWeekTitle(),
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

    public Visibility WeekViewVisibility => _displayMode == CalendarDisplayMode.Week
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool IsMonthView => _displayMode == CalendarDisplayMode.Month;

    public bool IsWeekView => _displayMode == CalendarDisplayMode.Week;

    public GridLength CalendarGridRowHeight => _displayMode == CalendarDisplayMode.Week
        ? new GridLength(0)
        : new GridLength(264);

    public DateOnly SelectedDate => _selectedDate;

    public GridLength WeekdayHeaderRowHeight => _displayMode == CalendarDisplayMode.Week
        ? new GridLength(0)
        : new GridLength(30);

    public GridLength NavigationTitleRowHeight => new(40);

    public Visibility NavigationTitleVisibility => Visibility.Visible;


    public string PreviousStepToolTip => _displayMode switch
    {
        CalendarDisplayMode.Week => "Previous week",
        CalendarDisplayMode.Year => "Previous year",
        CalendarDisplayMode.Decade => "Previous decade",
        _ => "Previous month"
    };

    public string NextStepToolTip => _displayMode switch
    {
        CalendarDisplayMode.Week => "Next week",
        CalendarDisplayMode.Year => "Next year",
        CalendarDisplayMode.Decade => "Next decade",
        _ => "Next month"
    };

    public GridLength SideToggleColumnWidth => new(SideToggleWidth);

    public GridLength SidePanelColumnWidth => _sidePanelExpanded
        ? new GridLength(SidePanelWidth)
        : new GridLength(0);

    public Visibility PrayerFeatureVisibility => _prayerTimesEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SidePanelVisibility => _sidePanelExpanded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string SideToggleContent => _sidePanelExpanded ? ">" : "<";

    public string SideToggleToolTip => _sidePanelExpanded
        ? "Hide side panel"
        : "Show day view";

    public bool IsDayPaneActive => _sidePaneMode == SidePaneMode.Day;

    public bool IsPrayerPaneActive => _sidePaneMode == SidePaneMode.Prayer;

    public Visibility SideTabsVisibility => _sidePaneMode == SidePaneMode.Details
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility DayPaneVisibility => _sidePaneMode == SidePaneMode.Day
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PrayerPaneVisibility => _sidePaneMode == SidePaneMode.Prayer && _prayerTimesEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DetailsPaneVisibility => _sidePaneMode == SidePaneMode.Details && SelectedEvent is not null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SideFooterVisibility => _sidePaneMode == SidePaneMode.Day
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DayListVisibility => _dayViewLayout.Equals("Timeline", StringComparison.OrdinalIgnoreCase)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility DayTimelineVisibility => _dayViewLayout.Equals("Timeline", StringComparison.OrdinalIgnoreCase)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DayListEmptyVisibility =>
        DayListVisibility == Visibility.Visible && SelectedDayEvents.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    public CalendarEventViewModel? SelectedEvent
    {
        get => _selectedEvent;
        private set
        {
            if (ReferenceEquals(_selectedEvent, value)) return;
            _selectedEvent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DetailsPaneVisibility));
        }
    }


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
        if (_displayMode is CalendarDisplayMode.Year or CalendarDisplayMode.Decade)
        {
            _displayMode = CalendarDisplayMode.Month;
        }
        if (_sidePanelExpanded) SetSidePaneMode(SidePaneMode.Day);
        RefreshCalendar();
        if (_displayMode == CalendarDisplayMode.Week)
        {
            Dispatcher.BeginInvoke(() => WeekTimeline.ScrollToCurrentTime(), DispatcherPriority.Loaded);
        }
        if (_sidePanelExpanded)
        {
            Dispatcher.BeginInvoke(() => DayTimeline.ScrollToCurrentTime(), DispatcherPriority.Loaded);
        }
    }

    private void ShowMonthView()
    {
        _visibleMonth = CalendarGridBuilder.FirstDayOfMonth(_selectedDate);
        _displayMode = CalendarDisplayMode.Month;
        RefreshCalendar();
    }

    private void ShowWeekView()
    {
        _visibleMonth = CalendarGridBuilder.FirstDayOfMonth(_selectedDate);
        _displayMode = CalendarDisplayMode.Week;
        RefreshCalendar();
        Dispatcher.BeginInvoke(() => WeekTimeline.ScrollToCurrentTime(), DispatcherPriority.Loaded);
    }

    private async Task LoadPrayerSettingsAsync()
    {
        var settings = await _syncCoordinator.SettingsStore.LoadAsync();
        if (_closed) return;
        ApplyPersistedPopupSize(settings);
        _prayerTimesEnabled = settings.PrayerTimesEnabled;
        _dayViewLayout = settings.DayViewLayout;
        _prayerLocation = new PrayerTimesLocation(
            settings.PrayerLocationName,
            settings.PrayerLatitude,
            settings.PrayerLongitude);

        if (!_prayerTimesEnabled)
        {
            if (_sidePaneMode == SidePaneMode.Prayer) SetSidePaneMode(SidePaneMode.Day);
        }

        OnPropertyChanged(nameof(DayListVisibility));
        OnPropertyChanged(nameof(DayTimelineVisibility));
        OnPropertyChanged(nameof(DayListEmptyVisibility));
        UpdateSideLayout(reposition: IsVisible);
        if (IsVisible) _uiTimer.Start();
        else _uiTimer.Stop();
        RefreshPrayerPanel();
        RefreshTimelineViews();
    }

    private void ToggleSidePanel()
    {
        if (_closed) return;
        _sidePanelExpanded = !_sidePanelExpanded;
        if (!_sidePanelExpanded) _uiTimer.Interval = TimeSpan.FromSeconds(30);
        if (_sidePanelExpanded)
        {
            SetSidePaneMode(SidePaneMode.Day);
        }

        UpdateSideLayout(reposition: IsVisible);
        RefreshPrayerPanel();
    }

    private void ShowDayPane() => SetSidePaneMode(SidePaneMode.Day);

    private void ShowPrayerPane()
    {
        if (_prayerTimesEnabled) SetSidePaneMode(SidePaneMode.Prayer);
    }

    private void SetSidePaneMode(SidePaneMode mode)
    {
        if (mode == SidePaneMode.Prayer && !_prayerTimesEnabled) mode = SidePaneMode.Day;
        _sidePaneMode = mode;
        _uiTimer.Interval = TimeSpan.FromSeconds(mode == SidePaneMode.Prayer && _sidePanelExpanded ? 1 : 30);
        if (mode != SidePaneMode.Details) SelectedEvent = null;
        OnPropertyChanged(nameof(IsDayPaneActive));
        OnPropertyChanged(nameof(IsPrayerPaneActive));
        OnPropertyChanged(nameof(SideTabsVisibility));
        OnPropertyChanged(nameof(DayPaneVisibility));
        OnPropertyChanged(nameof(PrayerPaneVisibility));
        OnPropertyChanged(nameof(DetailsPaneVisibility));
        OnPropertyChanged(nameof(SideFooterVisibility));
        if (mode == SidePaneMode.Day) RefreshTimelineViews();
        if (mode == SidePaneMode.Prayer) RefreshPrayerPanel();
    }

    /// <summary>
    /// Restores the persisted popup size, clamped to the work area of the display the
    /// popup is on. Settings roam, so a size saved on a larger monitor must not place
    /// the window off-screen here.
    /// </summary>
    private void ApplyPersistedPopupSize(SyncSettings settings)
    {
        var workArea = PopupPositioner.GetWorkArea(this);
        var maxWidth = Math.Max(MinWidth, workArea.Width - 24d);
        var maxHeight = Math.Max(MinHeight, workArea.Height - 24d);

        _baseWindowWidth = Math.Clamp(
            PopupSize.NormalizeWidth(settings.PopupWidth),
            PopupSize.MinWidth,
            Math.Max(PopupSize.MinWidth, maxWidth - SideChromeWidth));

        _applyingLayoutWidth = true;
        try
        {
            Height = Math.Clamp(PopupSize.NormalizeHeight(settings.PopupHeight), MinHeight, maxHeight);
        }
        finally
        {
            _applyingLayoutWidth = false;
        }

        _persistedPopupWidth = _baseWindowWidth;
        _persistedPopupHeight = Height;
    }

    private void ResizeTop_DragDelta(object sender, Primitives.DragDeltaEventArgs e) =>
        ResizeFromTopLeft(0d, e.VerticalChange);

    private void ResizeLeft_DragDelta(object sender, Primitives.DragDeltaEventArgs e) =>
        ResizeFromTopLeft(e.HorizontalChange, 0d);

    private void ResizeTopLeft_DragDelta(object sender, Primitives.DragDeltaEventArgs e) =>
        ResizeFromTopLeft(e.HorizontalChange, e.VerticalChange);

    /// <summary>
    /// Resizes against a fixed bottom-right corner, which is the edge the popup is
    /// anchored to. Enabling the OS sizing frame instead would hand the window a 7px
    /// non-client border that Windows paints over the borderless chrome.
    /// </summary>
    private void ResizeFromTopLeft(double horizontalChange, double verticalChange)
    {
        if (_closed) return;

        var workArea = PopupPositioner.GetWorkArea(this);
        var currentWidth = double.IsFinite(Width) ? Width : ActualWidth;
        var currentHeight = double.IsFinite(Height) ? Height : ActualHeight;
        if (!double.IsFinite(currentWidth) || !double.IsFinite(currentHeight)) return;

        if (horizontalChange != 0d)
        {
            // Cap so the left edge cannot cross the work area; the right edge is fixed.
            var widthLimit = Math.Min(
                PopupSize.MaxWidth + SideChromeWidth,
                Math.Max(MinWidth, Left + currentWidth - workArea.Left - 8d));
            var newWidth = Math.Clamp(currentWidth - horizontalChange, MinWidth, widthLimit);
            Left += currentWidth - newWidth;
            Width = newWidth;
        }

        if (verticalChange != 0d)
        {
            var heightLimit = Math.Min(
                PopupSize.MaxHeight,
                Math.Max(MinHeight, Top + currentHeight - workArea.Top - 8d));
            var newHeight = Math.Clamp(currentHeight - verticalChange, MinHeight, heightLimit);
            Top += currentHeight - newHeight;
            Height = newHeight;
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_closed || _applyingLayoutWidth) return;

        // A drag changes the whole window; the prayer chrome keeps its natural width,
        // so everything above it belongs to the calendar body.
        if (e.WidthChanged)
        {
            var body = ActualWidth - SideChromeWidth;
            if (double.IsFinite(body) && body > 0d) _baseWindowWidth = body;
        }

        QueuePopupSizeSave();
    }

    /// <summary>
    /// WPF raises SizeChanged continuously while dragging, and each save rewrites the
    /// whole settings document, so coalesce onto one write after the drag settles.
    /// </summary>
    private void QueuePopupSizeSave()
    {
        _popupSizeSaveTimer ??= CreatePopupSizeSaveTimer();
        _popupSizeSaveTimer.Stop();
        _popupSizeSaveTimer.Start();
    }

    /// <summary>
    /// Writes a pending size immediately. The popup hides on deactivation, which is the
    /// common way a drag ends, and the debounce timer would otherwise still be pending.
    /// </summary>
    private void FlushPendingPopupSizeSave()
    {
        if (_popupSizeSaveTimer is not { IsEnabled: true }) return;
        _popupSizeSaveTimer.Stop();
        _ = RunUiOperationAsync(SavePopupSizeAsync);
    }

    private DispatcherTimer CreatePopupSizeSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = RunUiOperationAsync(SavePopupSizeAsync);
        };
        return timer;
    }

    private async Task SavePopupSizeAsync()
    {
        if (_closed) return;

        var width = _baseWindowWidth;
        var height = Height;
        if (!double.IsFinite(width) || width <= 0d) width = ActualWidth - SideChromeWidth;
        if (!double.IsFinite(height) || height <= 0d) height = ActualHeight;
        if (!double.IsFinite(width) || !double.IsFinite(height)) return;

        width = PopupSize.NormalizeWidth(width);
        height = PopupSize.NormalizeHeight(height);
        if (NearlyEquals(width, _persistedPopupWidth) && NearlyEquals(height, _persistedPopupHeight)) return;

        // Re-read before writing: Settings is the other writer of this document, and a
        // stale copy here would discard whatever it just saved.
        var settings = await _syncCoordinator.SettingsStore.LoadAsync();
        if (_closed) return;
        settings.PopupWidth = width;
        settings.PopupHeight = height;
        await _syncCoordinator.SettingsStore.SaveAsync(settings);

        _persistedPopupWidth = width;
        _persistedPopupHeight = height;

        // Deliberately no NotifySettingsChanged: it would reload settings and feed the
        // resulting side-pane resize back into this path.
    }

    private static bool NearlyEquals(double left, double right) => Math.Abs(left - right) < 0.5d;

    private double SideChromeWidth =>
        SideToggleWidth + (_sidePanelExpanded ? SidePanelWidth : 0);

    private void UpdateSideLayout(bool reposition)
    {
        // Measure the shift from the side-pane chrome alone. Diffing total width would
        // also absorb any width the user dragged to, sliding the popup sideways.
        var oldChrome = _appliedSideChromeWidth;
        var newChrome = SideChromeWidth;
        var workArea = PopupPositioner.GetWorkArea(this);
        var minimumTotalWidth = PopupSize.MinWidth + newChrome;
        var availableWidth = Math.Max(minimumTotalWidth, workArea.Width - 16d);
        var desiredTotalWidth = Math.Min(_baseWindowWidth + newChrome, availableWidth);

        _applyingLayoutWidth = true;
        try
        {
            MinWidth = minimumTotalWidth;
            Width = desiredTotalWidth;
        }
        finally
        {
            _applyingLayoutWidth = false;
        }

        _appliedSideChromeWidth = newChrome;

        if (reposition)
        {
            var delta = newChrome - oldChrome;
            Left = Clamp(Left - delta, workArea.Left + 8, workArea.Right - Width - 8);
        }

        OnPropertyChanged(nameof(SideToggleColumnWidth));
        OnPropertyChanged(nameof(SidePanelColumnWidth));
        OnPropertyChanged(nameof(PrayerFeatureVisibility));
        OnPropertyChanged(nameof(SidePanelVisibility));
        OnPropertyChanged(nameof(SideToggleContent));
        OnPropertyChanged(nameof(SideToggleToolTip));
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
            var selectedPrayerTimes = GetCachedPrayerTimes(_selectedDate, timeZone);
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
            var dayPrayerTimes = GetCachedPrayerTimes(date, timeZone);
            var nextPrayer = dayPrayerTimes.Prayers
                .Where(IsCountdownPrayer)
                .FirstOrDefault(prayerTime => prayerTime.Time > now);

            if (nextPrayer is not null)
            {
                return nextPrayer;
            }
        }

        return GetCachedPrayerTimes(today.AddDays(1), timeZone)
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
        if (_displayMode == CalendarDisplayMode.Week)
        {
            var dayIndex = _selectedDate.DayNumber + (monthOffset * 7);
            if (dayIndex < DateOnly.MinValue.DayNumber || dayIndex > DateOnly.MaxValue.DayNumber)
            {
                return;
            }

            _selectedDate = DateOnly.FromDayNumber(dayIndex);
            _visibleMonth = CalendarGridBuilder.FirstDayOfMonth(_selectedDate);
            RefreshCalendar();
            Dispatcher.BeginInvoke(() => WeekTimeline.ScrollToCurrentTime(), DispatcherPriority.Loaded);
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
        if (_sidePanelExpanded) SetSidePaneMode(SidePaneMode.Day);
        RefreshCalendar();
        if (_sidePanelExpanded)
        {
            Dispatcher.BeginInvoke(() => DayTimeline.ScrollToCurrentTime(), DispatcherPriority.Loaded);
        }
    }

    private DailyPrayerTimes GetCachedPrayerTimes(DateOnly date, TimeZoneInfo zone)
    {
        if (!ReferenceEquals(zone, _prayerCacheZone) || _prayerCacheLocation != _prayerLocation)
        {
            _prayerCache.Clear();
            _prayerCacheZone = zone;
            _prayerCacheLocation = _prayerLocation;
        }
        if (_prayerCache.TryGetValue(date, out var cached)) return cached;
        if (_prayerCache.Count >= 4) _prayerCache.Clear();
        return _prayerCache[date] = _prayerTimesCalculator.Calculate(date, _prayerLocation, zone);
    }

    private void SelectTimelineDate(DateOnly date)
    {
        if (date.Year is < 2 or > 9998) return;
        _selectedDate = date;
        _visibleMonth = CalendarGridBuilder.FirstDayOfMonth(date);
        if (_sidePanelExpanded) SetSidePaneMode(SidePaneMode.Day);
        RefreshCalendar();
        Dispatcher.BeginInvoke(() =>
        {
            if (_displayMode == CalendarDisplayMode.Week) WeekTimeline.ScrollToCurrentTime();
            if (_sidePanelExpanded) DayTimeline.ScrollToCurrentTime();
        }, DispatcherPriority.Loaded);
    }

    private void ZoomOutCalendar()
    {
        _displayMode = _displayMode switch
        {
            CalendarDisplayMode.Week => CalendarDisplayMode.Month,
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
        OnPropertyChanged(nameof(WeekViewVisibility));
        OnPropertyChanged(nameof(IsMonthView));
        OnPropertyChanged(nameof(IsWeekView));
        OnPropertyChanged(nameof(CalendarGridRowHeight));
        OnPropertyChanged(nameof(WeekdayHeaderRowHeight));
        OnPropertyChanged(nameof(NavigationTitleRowHeight));
        OnPropertyChanged(nameof(NavigationTitleVisibility));
        OnPropertyChanged(nameof(PreviousStepToolTip));
        OnPropertyChanged(nameof(NextStepToolTip));
        OnPropertyChanged(nameof(AgendaEmptyText));
        OnPropertyChanged(nameof(AgendaEmptyVisibility));
        RefreshPrayerPanel();
        RefreshTimelineViews();
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
        var weekStart = GetWeekStart(selectedDate);
        var weekDates = Enumerable.Range(0, 7).Select(weekStart.AddDays).ToList();
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
        var weekEvents = weekDates
            .SelectMany(date => eventsByDate.TryGetValue(date, out var cachedEvents)
                ? cachedEvents
                : MergeEvents(ApplyCalendarNames(cache.GetEventsFor(date), sourceNames), localStore.GetEventsFor(date)))
            .GroupBy(calendarEvent => new
            {
                calendarEvent.SourceId,
                calendarEvent.Id,
                calendarEvent.Start,
                calendarEvent.End
            })
            .Select(group => group.First())
            .OrderBy(calendarEvent => calendarEvent.IsAllDay ? 0 : 1)
            .ThenBy(calendarEvent => calendarEvent.Start)
            .ThenBy(calendarEvent => calendarEvent.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

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

            _dayTimelineEvents = SelectedDayEvents.ToList();
            _weekTimelineEvents = weekEvents.Select(calendarEvent => new CalendarEventViewModel(calendarEvent)).ToList();
            RefreshTimelineViews();
            OnPropertyChanged(nameof(AgendaEmptyText));
            OnPropertyChanged(nameof(AgendaEmptyVisibility));
            OnPropertyChanged(nameof(DayListEmptyVisibility));
        });
    }

    private void RefreshTimelineViews()
    {
        if (!IsInitialized) return;

        DayTimeline.StartDate = _selectedDate;
        DayTimeline.DayCount = 1;
        DayTimeline.Events = _dayTimelineEvents;
        if (IsVisible && _sidePanelExpanded && IsDayPaneActive && DayTimelineVisibility == Visibility.Visible)
            DayTimeline.Refresh();

        WeekTimeline.StartDate = GetWeekStart(_selectedDate);
        WeekTimeline.DayCount = 7;
        WeekTimeline.Events = _weekTimelineEvents;
        if (IsVisible && IsWeekView) WeekTimeline.Refresh();
    }

    private static DateOnly GetWeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private string GetWeekTitle()
    {
        var start = GetWeekStart(_selectedDate);
        var end = start.AddDays(6);
        return start.Month == end.Month
            ? $"{start:dd} - {end:dd MMMM yyyy}"
            : $"{start:dd MMM} - {end:dd MMM yyyy}";
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
        SelectedEvent = calendarEvent;
        _sidePanelExpanded = true;
        SetSidePaneMode(SidePaneMode.Details);
        UpdateSideLayout(reposition: IsVisible);
    }

    private void CloseEventDetails()
    {
        if (_sidePaneMode == SidePaneMode.Details)
        {
            SetSidePaneMode(SidePaneMode.Day);
        }
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
        SetSidePaneMode(SidePaneMode.Day);
        await RefreshEventsFromCacheAsync();
    }

    private void NavigateEvent_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(SelectedEvent?.MapsUrl);

    private void JoinTeamsEvent_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(SelectedEvent?.TeamsMeetingUrl);

    private void OpenEventLink_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(SelectedEvent?.SourceUrl);

    private void DeleteEvent_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEvent is not { IsLocalEvent: true } calendarEvent) return;
        _keepOpenForChildWindow = true;
        MessageBoxResult result;
        try
        {
            result = MessageBox.Show(
                this,
                "Remove this local event?",
                "Delete event",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
        }
        finally
        {
            _keepOpenForChildWindow = false;
        }
        if (result == MessageBoxResult.Yes)
        {
            _ = RunUiOperationAsync(() => DeleteLocalEventAsync(calendarEvent));
        }
    }

    private static void OpenUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppDiagnostics.Log("Open event web link", ex);
            MessageBox.Show(
                "The link could not be opened. Check your default browser.",
                "Week Calendar",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
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
        _uiTimer.Stop();
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
