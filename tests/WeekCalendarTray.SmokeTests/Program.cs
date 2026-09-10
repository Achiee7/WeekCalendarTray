using System.Net;
using System.Text;
using WeekCalendarTray.Core;

const string PseudoGoogleIcs = """
BEGIN:VCALENDAR
PRODID:-//Google Inc//Google Calendar 70.9054//EN
VERSION:2.0
CALSCALE:GREGORIAN
METHOD:PUBLISH
BEGIN:VEVENT
DTSTART:20260508T070000Z
DTEND:20260508T080000Z
DTSTAMP:20260501T120000Z
UID:timed@google.example
SUMMARY:Google timed meeting
LOCATION:Office
END:VEVENT
BEGIN:VEVENT
DTSTART;VALUE=DATE:20260508
DTEND;VALUE=DATE:20260509
DTSTAMP:20260501T120000Z
UID:allday@google.example
SUMMARY:Google all-day release
END:VEVENT
BEGIN:VEVENT
DTSTART:20260504T090000Z
DTEND:20260504T093000Z
DTSTAMP:20260501T120000Z
RRULE:FREQ=WEEKLY;COUNT=3
UID:weekly@google.example
SUMMARY:Google weekly standup
END:VEVENT
END:VCALENDAR
""";

const string PseudoOutlookIcs = """
BEGIN:VCALENDAR
PRODID:-//Microsoft Corporation//Outlook 16.0 MIMEDIR//EN
VERSION:2.0
METHOD:PUBLISH
X-WR-CALNAME:Agenda
BEGIN:VEVENT
DTSTART:20270101T090000Z
DTEND:20270101T100000Z
DTSTAMP:20260501T120000Z
UID:future@outlook.example
SUMMARY:Future Outlook meeting
END:VEVENT
BEGIN:VEVENT
DTSTART:20260512T120000Z
DTEND:20260512T130000Z
DTSTAMP:20260501T120000Z
UID:current@outlook.example
SUMMARY:Outlook current meeting
LOCATION:Teams
DESCRIPTION:Bring drawings and notes.
ORGANIZER;CN=Ahmed Amer:mailto:ahmed@example.com
END:VEVENT
END:VCALENDAR
""";

try
{
    await PersistenceTests.RunAsync();
    IsoWeekNumbersFollowYearBoundaries();
    May2026RendersWithTodayInWeek19();
    CalendarAlwaysRendersCompleteMondaySundayRows();
    OverflowDaysAreMarkedOutsideCurrentMonth();
    CalendarEventsOccurOnExpectedDays();
    PseudoGoogleIcalFeedParsesTimedAllDayAndRecurringEvents();
    await PseudoGoogleIcalLinkFetchesThroughHttpClient();
    OutlookStyleIcalFeedDoesNotStopAtOutOfRangeFutureEvent();
    HtmlCalendarContentIsRejectedWithHelpfulMessage();
    PrayerTimesForLeerdamStayCloseToKoranReference();

    Console.WriteLine("All smoke tests passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void IsoWeekNumbersFollowYearBoundaries()
{
    AssertEqual(53, CalendarGridBuilder.GetIsoWeekNumber(new DateOnly(2020, 12, 31)), "2020-12-31 should be ISO week 53.");
    AssertEqual(53, CalendarGridBuilder.GetIsoWeekNumber(new DateOnly(2021, 1, 1)), "2021-01-01 should be ISO week 53.");
    AssertEqual(1, CalendarGridBuilder.GetIsoWeekNumber(new DateOnly(2021, 1, 4)), "2021-01-04 should be ISO week 1.");
    AssertEqual(1, CalendarGridBuilder.GetIsoWeekNumber(new DateOnly(2026, 1, 1)), "2026-01-01 should be ISO week 1.");
}

static void May2026RendersWithTodayInWeek19()
{
    var today = new DateOnly(2026, 5, 8);
    var grid = CalendarGridBuilder.CreateMonth(new DateOnly(2026, 5, 1), selectedDate: today, today: today);
    var may8 = grid.Weeks.SelectMany(week => week.Days).Single(day => day.Date == today);

    AssertEqual(19, may8.IsoWeekNumber, "2026-05-08 should be ISO week 19.");
    AssertTrue(may8.IsToday, "2026-05-08 should be marked as today.");
    AssertTrue(may8.IsSelected, "2026-05-08 should be marked as selected.");
    AssertTrue(may8.IsCurrentMonth, "2026-05-08 should be marked as part of May.");
}

static void CalendarAlwaysRendersCompleteMondaySundayRows()
{
    var grid = CalendarGridBuilder.CreateMonth(new DateOnly(2026, 5, 1), selectedDate: new DateOnly(2026, 5, 8), today: new DateOnly(2026, 5, 8));

    AssertEqual(6, grid.Weeks.Count, "Calendar should render six complete rows.");
    foreach (var week in grid.Weeks)
    {
        AssertEqual(7, week.Days.Count, "Each calendar row should contain seven days.");
        AssertEqual(DayOfWeek.Monday, week.Days[0].Date.DayOfWeek, "Each row should start on Monday.");
        AssertEqual(DayOfWeek.Sunday, week.Days[^1].Date.DayOfWeek, "Each row should end on Sunday.");
    }

    AssertEqual(new DateOnly(2026, 4, 27), grid.Weeks[0].Days[0].Date, "May 2026 grid should start on Monday 2026-04-27.");
    AssertEqual(new DateOnly(2026, 6, 7), grid.Weeks[^1].Days[^1].Date, "May 2026 grid should end on Sunday 2026-06-07.");
}

static void OverflowDaysAreMarkedOutsideCurrentMonth()
{
    var grid = CalendarGridBuilder.CreateMonth(new DateOnly(2026, 5, 1), selectedDate: new DateOnly(2026, 5, 8), today: new DateOnly(2026, 5, 8));
    var days = grid.Weeks.SelectMany(week => week.Days).ToList();

    AssertTrue(days.Where(day => day.Date.Month == 4).All(day => !day.IsCurrentMonth), "April overflow days should be muted.");
    AssertTrue(days.Where(day => day.Date.Month == 6).All(day => !day.IsCurrentMonth), "June overflow days should be muted.");
    AssertTrue(days.Where(day => day.Date.Month == 5).All(day => day.IsCurrentMonth), "May days should be marked as current month.");
}

static void CalendarEventsOccurOnExpectedDays()
{
    var timedEvent = new CalendarEvent(
        "test",
        "Test",
        "1",
        "Timed",
        new DateTimeOffset(2026, 5, 8, 9, 0, 0, TimeSpan.FromHours(2)),
        new DateTimeOffset(2026, 5, 8, 10, 0, 0, TimeSpan.FromHours(2)),
        false,
        null,
        null,
        null,
        null);

    var allDayEvent = new CalendarEvent(
        "test",
        "Test",
        "2",
        "All day",
        new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.FromHours(2)),
        new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.FromHours(2)),
        true,
        null,
        null,
        null,
        null);

    AssertTrue(timedEvent.OccursOn(new DateOnly(2026, 5, 8)), "Timed event should occur on its start day.");
    AssertTrue(!timedEvent.OccursOn(new DateOnly(2026, 5, 9)), "Timed event should not occur on the next day.");
    AssertTrue(allDayEvent.OccursOn(new DateOnly(2026, 5, 8)), "All-day event should occur on its first day.");
    AssertTrue(allDayEvent.OccursOn(new DateOnly(2026, 5, 9)), "All-day event should occur until its exclusive end date.");
    AssertTrue(!allDayEvent.OccursOn(new DateOnly(2026, 5, 10)), "All-day event should not include its exclusive end date.");
}

static void PseudoGoogleIcalFeedParsesTimedAllDayAndRecurringEvents()
{
    var subscription = new IcalSubscription
    {
        Id = "google-test",
        Name = "Pseudo Google",
        Url = "https://calendar.google.com/calendar/ical/fake/basic.ics"
    };

    var events = IcalFeedParser.Parse(
        PseudoGoogleIcs,
        subscription,
        new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

    AssertEqual(5, events.Count, "Pseudo Google feed should produce timed, all-day, and three recurring events.");
    AssertTrue(events.Any(calendarEvent => calendarEvent.Title == "Google timed meeting" && calendarEvent.Location == "Office"), "Timed Google event should parse.");

    var allDay = events.Single(calendarEvent => calendarEvent.Title == "Google all-day release");
    AssertTrue(allDay.IsAllDay, "Google all-day event should be marked all-day.");
    AssertTrue(allDay.OccursOn(new DateOnly(2026, 5, 8)), "Google all-day event should occur on May 8.");
    AssertTrue(!allDay.OccursOn(new DateOnly(2026, 5, 9)), "One-day Google all-day event should not occur on May 9.");

    var recurringDates = events
        .Where(calendarEvent => calendarEvent.Title == "Google weekly standup")
        .Select(calendarEvent => DateOnly.FromDateTime(calendarEvent.Start.LocalDateTime))
        .ToList();

    AssertTrue(
        recurringDates.SequenceEqual([new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 18)]),
        "Recurring Google weekly event should expand into three Mondays.");
}

static async Task PseudoGoogleIcalLinkFetchesThroughHttpClient()
{
    using var httpClient = new HttpClient(new StaticIcsHandler(PseudoGoogleIcs));
    var client = new IcalCalendarClient(httpClient);
    var subscription = new IcalSubscription
    {
        Id = "google-link",
        Name = "Pseudo Google Link",
        Url = "https://calendar.google.com/calendar/ical/fake/private-basic.ics"
    };

    var events = await client.FetchEventsAsync(
        subscription,
        new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        CancellationToken.None);

    AssertEqual(5, events.Count, "Pseudo Google iCal link should fetch and parse five events.");
}

static void OutlookStyleIcalFeedDoesNotStopAtOutOfRangeFutureEvent()
{
    var subscription = new IcalSubscription
    {
        Id = "outlook-test",
        Name = "Pseudo Outlook",
        Url = "https://outlook.office365.com/owa/calendar/fake/calendar.ics"
    };

    var events = IcalFeedParser.Parse(
        PseudoOutlookIcs,
        subscription,
        new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

    AssertEqual(1, events.Count, "Outlook feed should keep scanning after an out-of-range future event.");
    AssertEqual("Outlook current meeting", events[0].Title, "Outlook current meeting should be included.");
    AssertEqual("Teams", events[0].Location, "Outlook meeting location should parse.");
    AssertEqual("Bring drawings and notes.", events[0].Description, "Outlook meeting description should parse.");
    AssertEqual("Ahmed Amer", events[0].Organizer, "Outlook meeting organizer should parse.");
    AssertEqual(TimeSpan.FromHours(1), events[0].End - events[0].Start, "Outlook meeting should keep its one-hour duration.");
}

static void HtmlCalendarContentIsRejectedWithHelpfulMessage()
{
    var subscription = new IcalSubscription
    {
        Id = "outlook-html",
        Name = "Outlook HTML",
        Url = "https://outlook.office365.com/owa/calendar/fake/calendar.html"
    };

    try
    {
        IcalFeedParser.Parse(
            "<html><body>This is not an ICS calendar.</body></html>",
            subscription,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("ICS", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    throw new InvalidOperationException("HTML calendar content should be rejected with an ICS-specific message.");
}

static void PrayerTimesForLeerdamStayCloseToKoranReference()
{
    var calculator = new PrayerTimesCalculator();
    var location = new PrayerTimesLocation("Leerdam, NL", 51.8936d, 5.0913d);
    var date = new DateOnly(2026, 5, 26);
    var prayerTimes = calculator.Calculate(date, location, GetAmsterdamTimeZone());
    var timesByName = prayerTimes.Prayers.ToDictionary(prayer => prayer.Name, StringComparer.OrdinalIgnoreCase);

    AssertWithinMinutes("03:44", timesByName["Fajr"].Time, 8, "Leerdam Fajr should stay close to the Koran.nl reference.");
    AssertWithinMinutes("05:32", timesByName["Shuruq"].Time, 4, "Leerdam Shuruq should stay close to the Koran.nl reference.");
    AssertWithinMinutes("13:37", timesByName["Dhor"].Time, 4, "Leerdam Dhor should stay close to the Koran.nl reference.");
    AssertWithinMinutes("17:56", timesByName["Asr"].Time, 5, "Leerdam Asr should stay close to the Koran.nl reference.");
    AssertWithinMinutes("21:43", timesByName["Maghrib"].Time, 4, "Leerdam Maghrib should stay close to the Koran.nl reference.");
    AssertWithinMinutes("23:26", timesByName["Isha"].Time, 6, "Leerdam Isha should stay close to the Koran.nl reference.");
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertWithinMinutes(string expectedTime, DateTimeOffset actual, int toleranceMinutes, string message)
{
    var expected = TimeOnly.ParseExact(expectedTime, "HH:mm");
    var expectedMinutes = (expected.Hour * 60) + expected.Minute;
    var actualMinutes = (actual.Hour * 60) + actual.Minute;
    var difference = Math.Abs(expectedMinutes - actualMinutes);
    difference = Math.Min(difference, 1440 - difference);

    if (difference > toleranceMinutes)
    {
        throw new InvalidOperationException($"{message} Expected near: {expectedTime}. Actual: {actual:HH:mm}.");
    }
}

static TimeZoneInfo GetAmsterdamTimeZone()
{
    foreach (var timeZoneId in new[] { "W. Europe Standard Time", "Europe/Amsterdam" })
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }
    }

    return TimeZoneInfo.Local;
}

sealed class StaticIcsHandler(string ics) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ics, Encoding.UTF8, "text/calendar")
        });
    }
}
