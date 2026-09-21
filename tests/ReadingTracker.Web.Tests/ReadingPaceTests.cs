using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public class ReadingPaceTests
{
    private static readonly DateOnly Today = new(2026, 9, 21);

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static LibraryEntry Entry(
        string status = "Reading",
        int addedDaysAgo = 30,
        DateOnly? finishedOn = null,
        int? pageCount = 300,
        decimal? amountRead = null,
        string? unit = "Pages",
        int? percent = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            status,
            "Pages",
            At(addedDaysAgo),
            finishedOn,
            null,
            pageCount,
            null,
            amountRead is null && percent is null ? null : new Progress(amountRead, amountRead is null ? null : unit, percent),
            null);

    private static ReadingSessionView Session(int daysAgo, decimal amount = 10, string unit = "Pages") =>
        new(Guid.NewGuid(), amount, unit, "Reader", At(daysAgo), null, null);

    /// <summary>Mid-morning on that day, so the day is the same in any nearby zone.</summary>
    private static DateTimeOffset At(int daysAgo) =>
        new(Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

    [Fact]
    public void A_book_with_no_sessions_started_the_day_it_was_added_and_has_no_rate()
    {
        var pace = ReadingPace.Of(Entry(addedDaysAgo: 5), [], Today, Utc);

        Assert.Equal(Today.AddDays(-5), pace.Started);
        Assert.Equal(5, pace.DaysReading);
        Assert.Null(pace.PagesADay);
        Assert.Equal(PaceGap.NotStarted, pace.NoRateBecause);
        Assert.Null(pace.FinishOn);
        Assert.Equal(PaceGap.NotStarted, pace.NoForecastBecause);
    }

    [Fact]
    public void One_session_today_is_one_day_reading_and_no_rate_yet()
    {
        var pace = ReadingPace.Of(Entry(amountRead: 40), [Session(0, 40)], Today, Utc);

        Assert.Equal(Today, pace.Started);
        Assert.Equal(1, pace.DaysReading);
        Assert.Equal(PaceGap.StartedToday, pace.NoRateBecause);
        Assert.Equal(PaceGap.StartedToday, pace.NoForecastBecause);
    }

    [Fact]
    public void A_normal_run_has_a_rate_over_the_days_since_the_first_session_and_a_forecast()
    {
        // 90 pages over the 10 days since the first session: 9 a day, 210 to go, 24 more days.
        var sessions = new[] { Session(10, 30), Session(6, 30), Session(1, 30) };

        var pace = ReadingPace.Of(Entry(pageCount: 300, amountRead: 90), sessions, Today, Utc);

        Assert.Equal(Today.AddDays(-10), pace.Started);
        Assert.Equal(10, pace.DaysReading);
        Assert.Equal(9m, pace.PagesADay);
        Assert.Null(pace.NoRateBecause);
        Assert.Equal(Today.AddDays(24), pace.FinishOn);
        Assert.Null(pace.NoForecastBecause);
    }

    [Fact]
    public void Started_is_the_earliest_session_whatever_order_they_came_in()
    {
        var sessions = new[] { Session(2, 10), Session(8, 10), Session(4, 10) };

        var pace = ReadingPace.Of(Entry(amountRead: 30), sessions, Today, Utc);

        Assert.Equal(Today.AddDays(-8), pace.Started);
    }

    [Fact]
    public void A_finished_book_is_measured_to_the_day_it_was_finished_and_has_no_forecast()
    {
        var finishedOn = Today.AddDays(-3);
        var sessions = new[] { Session(13, 100), Session(3, 200) };

        var pace = ReadingPace.Of(
            Entry(status: "Finished", finishedOn: finishedOn, pageCount: 300, amountRead: 300),
            sessions,
            Today,
            Utc);

        // Ten days from the first session to Finished on, not thirteen to today.
        Assert.Equal(10, pace.DaysReading);
        Assert.Equal(30m, pace.PagesADay);
        Assert.Null(pace.FinishOn);
        Assert.Equal(PaceGap.Finished, pace.NoForecastBecause);
    }

    [Fact]
    public void A_book_finished_the_day_it_was_started_took_one_day()
    {
        var pace = ReadingPace.Of(
            Entry(status: "Finished", finishedOn: Today, pageCount: 120, amountRead: 120),
            [Session(0, 120)],
            Today,
            Utc);

        Assert.Equal(1, pace.DaysReading);
        Assert.Equal(120m, pace.PagesADay);
    }

    [Fact]
    public void An_unknown_length_gives_no_rate_and_no_forecast()
    {
        var pace = ReadingPace.Of(Entry(pageCount: null, amountRead: 90), [Session(10, 90)], Today, Utc);

        Assert.Equal(90m, pace.PagesRead);
        Assert.Null(pace.PageCount);
        Assert.Equal(PaceGap.NoPageCount, pace.NoRateBecause);
        Assert.Equal(PaceGap.NoPageCount, pace.NoForecastBecause);
    }

    [Fact]
    public void An_amount_read_in_percent_gives_no_rate()
    {
        var pace = ReadingPace.Of(
            Entry(amountRead: 30, unit: "Percentage", percent: 30),
            [Session(10, 30, "Percentage")],
            Today,
            Utc);

        Assert.Null(pace.PagesRead);
        Assert.Equal(PaceGap.NotInPages, pace.NoRateBecause);
        Assert.Equal(10, pace.DaysReading);
    }

    [Fact]
    public void The_finish_date_rounds_a_part_day_up_to_the_next_whole_one()
    {
        // 100 pages in 8 days is 12.5 a day; 200 to go is 16 days exactly, 201 is 16.08 — so 17.
        var pace = ReadingPace.Of(Entry(pageCount: 301, amountRead: 100), [Session(8, 100)], Today, Utc);

        Assert.Equal(12.5m, pace.PagesADay);
        Assert.Equal(Today.AddDays(17), pace.FinishOn);
    }

    [Fact]
    public void A_book_read_to_the_end_but_not_marked_finished_is_forecast_for_today()
    {
        var pace = ReadingPace.Of(Entry(pageCount: 300, amountRead: 300), [Session(5, 300)], Today, Utc);

        Assert.Equal(Today, pace.FinishOn);
    }

    [Fact]
    public void Session_days_are_the_readers_days_not_utc()
    {
        // 23:30 UTC yesterday is already today in Kyiv (UTC+3), so the reader started today.
        var lateLastNight = new DateTimeOffset(Today.AddDays(-1).ToDateTime(new TimeOnly(23, 30)), TimeSpan.Zero);
        var kyiv = TimeZoneInfo.CreateCustomTimeZone("kyiv", TimeSpan.FromHours(3), "Kyiv", "Kyiv");
        var session = new ReadingSessionView(Guid.NewGuid(), 10, "Pages", "Reader", lateLastNight, null, null);

        var pace = ReadingPace.Of(Entry(amountRead: 10), [session], Today, kyiv);

        Assert.Equal(Today, pace.Started);
        Assert.Equal(PaceGap.StartedToday, pace.NoRateBecause);
    }
}
