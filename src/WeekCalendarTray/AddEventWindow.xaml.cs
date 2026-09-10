using System.Globalization;
using System.Windows;

namespace WeekCalendarTray;

public partial class AddEventWindow : Window
{
    internal AddEventWindow(DateOnly selectedDate)
    {
        InitializeComponent();
        EventDateTextBox.Text = selectedDate
            .ToDateTime(TimeOnly.MinValue)
            .ToString("d", CultureInfo.CurrentCulture);
        TitleTextBox.Focus();
    }

    internal LocalCalendarEvent? CreatedEvent { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            StatusText.Text = "Enter an event title.";
            return;
        }

        if (!DateTime.TryParse(EventDateTextBox.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var selectedDateTime))
        {
            StatusText.Text = "Use a valid date.";
            return;
        }

        var date = DateOnly.FromDateTime(selectedDateTime);
        var isAllDay = AllDayCheckBox.IsChecked == true;
        var start = CreateDateTimeOffset(date, TimeOnly.MinValue);
        var end = start.AddDays(1);

        if (!isAllDay)
        {
            if (!TryParseTime(StartTimeTextBox.Text, out var startTime)
                || !TryParseTime(EndTimeTextBox.Text, out var endTime))
            {
                StatusText.Text = "Use a valid start and end time, for example 09:00.";
                return;
            }

            start = CreateDateTimeOffset(date, startTime);
            end = CreateDateTimeOffset(date, endTime);
            if (end <= start)
            {
                StatusText.Text = "End time must be after the start time.";
                return;
            }
        }

        CreatedEvent = new LocalCalendarEvent
        {
            Title = title,
            Start = start,
            End = end,
            IsAllDay = isAllDay,
            Location = Clean(LocationTextBox.Text),
            Description = Clean(DescriptionTextBox.Text)
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void AllDayCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (TimeGrid is null)
        {
            return;
        }

        TimeGrid.IsEnabled = AllDayCheckBox.IsChecked != true;
    }

    private static DateTimeOffset CreateDateTimeOffset(DateOnly date, TimeOnly time)
    {
        var dateTime = date.ToDateTime(time);
        return new DateTimeOffset(dateTime, TimeZoneInfo.Local.GetUtcOffset(dateTime));
    }

    private static bool TryParseTime(string value, out TimeOnly time)
    {
        return TimeOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out time);
    }

    private static string? Clean(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
