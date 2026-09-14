using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using SystemColors = System.Windows.SystemColors;
using UserControl = System.Windows.Controls.UserControl;

namespace WeekCalendarTray;

public sealed class TimelineView : UserControl
{
    private const double TimeGutter = 48;
    private const double HourHeight = 60;
    private readonly Grid _content = new();
    private readonly Grid _header = new();
    private readonly Grid _allDay = new();
    private readonly Canvas _canvas = new() { Height = 24 * HourHeight, ClipToBounds = true };
    private readonly Canvas _now = new() { IsHitTestVisible = false, Height = 24 * HourHeight };
    private readonly ScrollViewer _vertical;
    private readonly ScrollViewer _horizontal;
    private readonly ScrollViewer _allDayScroll;
    private bool _initialScroll;
    private double _dayWidth;
    private readonly Line _nowLine = new() { Stroke = Brushes.IndianRed, StrokeThickness = 2 };
    private readonly Ellipse _nowDot = new() { Width = 8, Height = 8, Fill = Brushes.IndianRed };
    private readonly ControlTemplate _eventTemplate = EventTemplate();
    private IReadOnlyList<CalendarEventViewModel>? _renderedEvents;
    private DateOnly _renderedDate;
    private DateOnly _renderedToday;
    private int _renderedCount;
    private double _renderedWidth;
    private int _markerMinute = -1;
    internal int RenderCount { get; private set; }

    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public int DayCount { get; set; } = 1;
    public IReadOnlyList<CalendarEventViewModel> Events { get; set; } = [];
    public DateOnly SelectedEventDate { get; private set; }
    public event EventHandler<CalendarEventViewModel>? EventSelected;
    public event EventHandler<DateOnly>? DateSelected;

    internal IReadOnlyList<TimelineEntry> Entries { get; private set; } = [];
    internal double DayWidth => _dayWidth;
    internal double VerticalOffset => _vertical.VerticalOffset;
    internal bool HasCurrentTimeMarker => _now.Visibility == Visibility.Visible;

    public TimelineView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition());
        _header.Margin = new Thickness(0, 0, 18, 0);
        _allDay.Margin = new Thickness(0, 0, 18, 0);
        _content.Children.Add(_header);
        _allDayScroll = new ScrollViewer
        {
            Content = _allDay, MaxHeight = 94,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(_allDayScroll, 1);
        _content.Children.Add(_allDayScroll);
        var body = new Grid();
        body.Children.Add(_canvas);
        body.Children.Add(_now);
        _now.Children.Add(_nowLine);
        _now.Children.Add(_nowDot);
        _now.Visibility = Visibility.Collapsed;
        _vertical = new ScrollViewer
        {
            Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false
        };
        AutomationProperties.SetName(_vertical, "Hours of the day");
        Grid.SetRow(_vertical, 2);
        _content.Children.Add(_vertical);
        _horizontal = new ScrollViewer
        {
            Content = _content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Content = _horizontal;
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");
        SizeChanged += (_, _) => { if (IsVisible) Refresh(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
            else ReleaseVisuals();
        };
        Loaded += (_, _) =>
        {
            ThemeManager.ThemeChanged += ThemeChanged;
            if (IsVisible) Refresh();
            if (!_initialScroll) { ScrollToCurrentTime(); _initialScroll = true; }
        };
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= ThemeChanged;
    }

    private void ThemeChanged(object? sender, EventArgs e)
    {
        _renderedWidth = 0;
        if (IsVisible) Refresh();
    }

    internal void ReleaseVisuals()
    {
        _header.Children.Clear();
        _allDay.Children.Clear();
        _canvas.Children.Clear();
        Entries = [];
        _renderedEvents = null;
        _renderedWidth = 0;
        _now.Visibility = Visibility.Collapsed;
        _markerMinute = -1;
    }

    public void Refresh()
    {
        var count = Math.Clamp(DayCount, 1, 7);
        var width = Math.Max(Math.Max(0, ActualWidth) - 1, TimeGutter + count * 90 + 18);
        _content.Width = width;
        _content.Height = Math.Max(0, ActualHeight - (_horizontal.ComputedHorizontalScrollBarVisibility == Visibility.Visible ? 18 : 0));
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (ReferenceEquals(_renderedEvents, Events) && _renderedDate == StartDate
            && _renderedCount == count && _renderedToday == today && Math.Abs(_renderedWidth - width) < 0.5)
        {
            RefreshCurrentTime();
            return;
        }
        var reuseLayout = ReferenceEquals(_renderedEvents, Events) && _renderedDate == StartDate && _renderedCount == count;
        _renderedEvents = Events;
        _renderedDate = StartDate;
        _renderedCount = count;
        _renderedWidth = width;
        _renderedToday = today;
        _markerMinute = -1;
        RenderCount++;
        _dayWidth = (width - TimeGutter - 18) / count;
        BuildColumns(_header, count);
        BuildColumns(_allDay, count);
        _header.Children.Clear();
        _allDay.Children.Clear();
        _canvas.Children.Clear();

        var zone = Text(DateTimeOffset.Now.ToString("zzz", CultureInfo.InvariantCulture), 9, "MutedTextBrush");
        zone.VerticalAlignment = VerticalAlignment.Center;
        _header.Children.Add(zone);
        var hasAllDay = false;
        for (var day = 0; day < count; day++)
        {
            var date = StartDate.AddDays(day);
            var header = new Button
            {
                Content = date.ToDateTime(TimeOnly.MinValue).ToString("ddd\nd MMM", CultureInfo.CurrentCulture),
                Height = 44, Padding = new Thickness(2), BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, FontSize = 11,
                FontWeight = date == DateOnly.FromDateTime(DateTime.Now) ? FontWeights.Bold : FontWeights.Normal
            };
            header.SetResourceReference(ForegroundProperty, date == DateOnly.FromDateTime(DateTime.Now) ? "AccentBrush" : "PrimaryTextBrush");
            header.Click += (_, _) => DateSelected?.Invoke(this, date);
            AutomationProperties.SetName(header, date.ToLongDateString());
            Grid.SetColumn(header, day + 1);
            _header.Children.Add(header);
            var stack = new StackPanel { Margin = new Thickness(2, 0, 2, 4) };
            foreach (var item in Events.Where(e => e.IsAllDay && e.OccursOn(date))
                .DistinctBy(e => (e.SourceId, e.Id, e.Start, e.End)))
            {
                hasAllDay = true;
                var button = EventButton(item, 24, _dayWidth - 4, date);
                button.Height = 24;
                button.Margin = new Thickness(0, 1, 0, 1);
                stack.Children.Add(button);
            }
            Grid.SetColumn(stack, day + 1);
            _allDay.Children.Add(stack);
        }
        _allDay.Children.Add(Text("All day", 10, "MutedTextBrush"));
        _allDayScroll.Visibility = hasAllDay ? Visibility.Visible : Visibility.Collapsed;

        for (var hour = 0; hour <= 24; hour++)
        {
            var y = hour * HourHeight;
            var line = new Line { X1 = TimeGutter, X2 = width - 18, Y1 = y, Y2 = y, StrokeThickness = 1 };
            line.SetResourceReference(Shape.StrokeProperty, "WeekDividerBrush");
            _canvas.Children.Add(line);
            if (hour == 24) continue;
            var label = Text($"{hour:00}:00", 10, "SecondaryTextBrush");
            Canvas.SetLeft(label, 1);
            Canvas.SetTop(label, Math.Max(1, y - 7));
            _canvas.Children.Add(label);
        }
        for (var day = 0; day <= count; day++)
        {
            var x = TimeGutter + day * _dayWidth;
            var line = new Line { X1 = x, X2 = x, Y1 = 0, Y2 = 24 * HourHeight, StrokeThickness = 1 };
            line.SetResourceReference(Shape.StrokeProperty, "DividerBrush");
            _canvas.Children.Add(line);
        }
        if (!reuseLayout) Entries = TimelineLayout.Create(StartDate, count, Events);
        foreach (var entry in Entries)
        {
            var height = (entry.EndMinute - entry.StartMinute) * HourHeight / 60 - 1;
            var blockWidth = Math.Max(1, (_dayWidth - 4) / entry.ColumnCount - 1);
            var button = EventButton(entry.Event, height, blockWidth, StartDate.AddDays(entry.Day));
            button.Width = blockWidth;
            button.Height = height;
            Canvas.SetLeft(button, TimeGutter + entry.Day * _dayWidth + 2 + entry.Column * (_dayWidth - 4) / entry.ColumnCount);
            Canvas.SetTop(button, entry.StartMinute * HourHeight / 60);
            _canvas.Children.Add(button);
        }
        RefreshCurrentTime();
    }

    public void RefreshCurrentTime()
    {
        var now = DateTime.Now;
        var day = DateOnly.FromDateTime(now).DayNumber - StartDate.DayNumber;
        if (day < 0 || day >= Math.Clamp(DayCount, 1, 7) || _dayWidth <= 0)
        {
            _now.Visibility = Visibility.Collapsed;
            _markerMinute = -1;
            return;
        }
        var minute = now.Hour * 60 + now.Minute;
        if (_markerMinute == minute && _now.Visibility == Visibility.Visible) return;
        _now.Visibility = Visibility.Visible;
        _markerMinute = minute;
        var y = now.TimeOfDay.TotalMinutes * HourHeight / 60;
        var x = TimeGutter + day * _dayWidth;
        _nowLine.X1 = x;
        _nowLine.X2 = x + _dayWidth;
        _nowLine.Y1 = _nowLine.Y2 = y;
        Canvas.SetLeft(_nowDot, x - 4);
        Canvas.SetTop(_nowDot, y - 4);
        AutomationProperties.SetName(_now, $"Current time {now:t}");
    }

    public void ScrollToCurrentTime()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
        {
            var now = DateTime.Now;
            var day = DateOnly.FromDateTime(now).DayNumber - StartDate.DayNumber;
            var minute = day >= 0 && day < DayCount ? now.TimeOfDay.TotalMinutes : 8 * 60;
            _vertical.ScrollToVerticalOffset(Math.Max(0, minute * HourHeight / 60 - 80));
        }));
    }

    private static void BuildColumns(Grid grid, int count)
    {
        grid.ColumnDefinitions.Clear();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimeGutter) });
        for (var i = 0; i < count; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
    }

    private Button EventButton(CalendarEventViewModel item, double height, double width, DateOnly date)
    {
        var stack = new StackPanel { Margin = new Thickness(5, 2, 3, 2) };
        stack.Children.Add(new TextBlock
        {
            Text = item.Title, FontSize = 11, FontWeight = FontWeights.SemiBold,
            TextWrapping = height >= 54 && width >= 100 ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = height >= 54 ? 30 : 16
        });
        if (height >= 40) stack.Children.Add(new TextBlock { Text = width >= 90 ? item.TimeText : item.Start.LocalDateTime.ToString("t", CultureInfo.CurrentCulture), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis });
        if (height >= 76 && width >= 110 && item.HasLocation) stack.Children.Add(new TextBlock { Text = item.Location, FontSize = 10, TextWrapping = TextWrapping.Wrap, MaxHeight = Math.Max(0, height - 54) });
        var background = SystemParameters.HighContrast ? SystemColors.HighlightBrush : item.AccentBrush;
        var color = (background as SolidColorBrush)?.Color ?? Colors.Teal;
        var foreground = SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : Contrast(color);
        var button = new Button
        {
            Content = stack, Background = background, Foreground = foreground,
            Padding = new Thickness(0), BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top, ClipToBounds = true,
            ToolTip = $"{item.Title}\n{item.TimeText}" + (item.HasLocation ? $"\n{item.Location}" : ""),
            Tag = item, Template = _eventTemplate
        };
        AutomationProperties.SetName(button, $"{item.Title}, {item.TimeText}, {item.Location}");
        button.Click += (_, _) =>
        {
            SelectedEventDate = date;
            EventSelected?.Invoke(this, item);
        };
        return button;
    }

    private static ControlTemplate EventTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.ClipToBoundsProperty, true);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentProperty));
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(OpacityProperty, 0.86));
        template.Triggers.Add(hover);
        return template;
    }

    private static SolidColorBrush Contrast(Color color)
    {
        static double Linear(byte channel) { var v = channel / 255d; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        var luminance = 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        return luminance > 0.179 ? Brushes.Black : Brushes.White;
    }

    private static TextBlock Text(string value, double size, string brush)
    {
        var text = new TextBlock { Text = value, FontSize = size, Margin = new Thickness(2), TextTrimming = TextTrimming.CharacterEllipsis };
        text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return text;
    }
}
