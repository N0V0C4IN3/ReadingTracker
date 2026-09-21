using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public class SessionDaysTests
{
    private static readonly DateOnly Today = new(2026, 9, 21); // a Monday

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static ReadingSessionView Session(int daysAgo, decimal amount, string unit = "Pages", int hour = 10, int? minutes = null, string source = "Reader") =>
        new(Guid.NewGuid(), amount, unit, source, new DateTimeOffset(Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero), minutes, null);

    [Fact]
    public void Groups_by_day_latest_day_first_and_latest_session_first_within_it()
    {
        var earlier = Session(1, 10, hour: 9);
        var later = Session(1, 20, hour: 21);
        var old = Session(5, 7);
        var today = Session(0, 3);

        var days = SessionDays.Of([earlier, old, later, today], 300, Utc);

        Assert.Equal([Today, Today.AddDays(-1), Today.AddDays(-5)], days.Select(day => day.Day));
        Assert.Equal([later, earlier], days[1].Sessions);
    }

    [Fact]
    public void A_report_just_after_midnight_lands_on_the_new_day_in_the_readers_zone()
    {
        // 21:20 UTC on the 20th is 00:20 on the 21st in Kyiv (UTC+3).
        var kyiv = TimeZoneInfo.CreateCustomTimeZone("kyiv", TimeSpan.FromHours(3), "Kyiv", "Kyiv");
        var report = new ReadingSessionView(Guid.NewGuid(), 2, "Percentage", "Device",
            new DateTimeOffset(new DateTime(2026, 9, 20, 21, 20, 0), TimeSpan.Zero), null, null);

        var days = SessionDays.Of([report], 300, kyiv);

        Assert.Equal(new DateOnly(2026, 9, 21), Assert.Single(days).Day);
    }

    [Fact]
    public void Names_today_yesterday_and_then_the_weekday_with_the_date()
    {
        var days = SessionDays.Of([Session(0, 1), Session(1, 1), Session(4, 1), Session(400, 1)], 300, Utc);

        Assert.Equal("Today", days[0].Name(Today));
        Assert.Equal("21 Sep", days[0].Date(Today));
        Assert.Equal("Yesterday", days[1].Name(Today));
        Assert.Equal("Thursday", days[2].Name(Today));
        Assert.Equal("17 Sep", days[2].Date(Today));
        Assert.Equal("17 Aug 2025", days[3].Date(Today));
    }

    [Fact]
    public void Totals_a_day_in_pages_where_it_can_and_in_minutes_where_given()
    {
        var day = Assert.Single(SessionDays.Of(
            [Session(0, 20, minutes: 25), Session(0, 5, "Percentage", hour: 12, minutes: 15), Session(0, 8, hour: 14)],
            200,
            Utc));

        // 20 pages, 5% of 200 = 10 pages, 8 pages.
        Assert.Equal(38m, day.Pages);
        Assert.Null(day.Percent);
        Assert.Equal(40, day.Minutes);
    }

    [Fact]
    public void Keeps_percent_it_cannot_convert_apart_and_says_nothing_of_minutes_nobody_gave()
    {
        var day = Assert.Single(SessionDays.Of([Session(0, 5, "Percentage"), Session(0, 8, hour: 14)], null, Utc));

        Assert.Equal(8m, day.Pages);
        Assert.Equal(5m, day.Percent);
        Assert.Null(day.Minutes);
    }

    [Fact]
    public void A_day_of_only_unconvertible_percent_has_no_page_total()
    {
        var day = Assert.Single(SessionDays.Of([Session(0, 5, "Percentage"), Session(0, 3, "Percentage", hour: 12)], null, Utc));

        Assert.Null(day.Pages);
        Assert.Equal(8m, day.Percent);
    }

    [Fact]
    public void Folds_past_twenty_days_until_asked_for_all()
    {
        var twentyOne = SessionDays.Of(Enumerable.Range(0, 21).Select(daysAgo => Session(daysAgo, 1)).ToList(), 300, Utc);
        var twenty = SessionDays.Of(Enumerable.Range(0, 20).Select(daysAgo => Session(daysAgo, 1)).ToList(), 300, Utc);

        Assert.True(SessionDays.Folded(twentyOne));
        Assert.Equal(20, SessionDays.Shown(twentyOne, all: false).Count);
        Assert.Equal(Today.AddDays(-19), SessionDays.Shown(twentyOne, all: false)[^1].Day);
        Assert.Equal(21, SessionDays.Shown(twentyOne, all: true).Count);
        Assert.False(SessionDays.Folded(twenty));
        Assert.Equal(20, SessionDays.Shown(twenty, all: false).Count);
    }
}
