using System.Diagnostics;
using System.Windows;

namespace WeekCalendarTray;

public partial class EventDetailsWindow : Window
{
    private CalendarEventViewModel _calendarEvent;

    internal EventDetailsWindow(CalendarEventViewModel calendarEvent)
    {
        _calendarEvent = calendarEvent;
        InitializeComponent();
        DataContext = calendarEvent;
    }

    internal event EventHandler<CalendarEventViewModel>? DeleteRequested;

    internal void UpdateEvent(CalendarEventViewModel calendarEvent)
    {
        _calendarEvent = calendarEvent;
        DataContext = calendarEvent;
        DetailsScrollViewer.ScrollToTop();
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_calendarEvent.SourceUrl);
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_calendarEvent.MapsUrl);
    }

    private void JoinTeams_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_calendarEvent.TeamsMeetingUrl);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "Remove this local event?",
            "Delete event",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteRequested?.Invoke(this, _calendarEvent);
        Close();
    }

    private static void OpenUrl(string url)
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
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppDiagnostics.Log("Open event web link", ex);
            System.Windows.MessageBox.Show("The link could not be opened. Check your default browser.",
                "Week Calendar", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Close();
        }
    }
}
