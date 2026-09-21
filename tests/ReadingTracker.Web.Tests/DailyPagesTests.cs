using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public class DailyPagesTests
{
    private static readonly DateOnly Today = new(2026, 9, 21);

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static ReadingSessionView Session(int daysAgo, decimal amount, string unit = "Pages", int hour = 10) =>
        new(Guid.NewGuid(), amount, unit, "Reader", new DateTimeOffset(Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero), null, null);

    [Fact]
    public void Is_twenty_one_days_ending_today_with_nothing_on_days_without_reading()
    {
        var series = DailyPages.Of([], 300, Today, Utc);

        Assert.Equal(DailyPages.WindowDays, series.Days.Count);
        Assert.Equal(Today.AddDays(-20), series.Days[0].Day);
        Assert.Equal(Today, series.Days[^1].Day);
        Assert.All(series.Days, day => Assert.Equal(0m, day.Pages));
        Assert.Equal(0, series.ReadingDays);
    }

    [Fact]
    public void Sums_the_pages_of_a_days_sessions()
    {
        var series = DailyPages.Of([Session(3, 12), Session(3, 8, hour: 21), Session(0, 5)], 300, Today, Utc);

        Assert.Equal(20m, series.Days[^4].Pages);
        Assert.Equal(5m, series.Days[^1].Pages);
        Assert.Equal(25m, series.Total);
        Assert.Equal(20m, series.Max);
        Assert.Equal(2, series.ReadingDays);
    }

    [Fact]
    public void Converts_percent_through_the_effective_page_count()
    {
        var series = DailyPages.Of([Session(1, 10, "Percentage")], 250, Today, Utc);

        Assert.Equal(25m, series.Days[^2].Pages);
        Assert.Equal(0, series.LeftOut);
    }

    [Fact]
    public void Leaves_out_percent_it_cannot_convert_and_says_how_many()
    {
        var series = DailyPages.Of([Session(1, 10, "Percentage"), Session(1, 4), Session(2, 5, "Percentage")], null, Today, Utc);

        Assert.Equal(4m, series.Days[^2].Pages);
        Assert.Equal(2, series.LeftOut);
    }

    [Fact]
    public void Ignores_sessions_before_the_window_without_counting_them_as_left_out()
    {
        var series = DailyPages.Of([Session(21, 50), Session(40, 10, "Percentage"), Session(20, 7)], null, Today, Utc);

        Assert.Equal(7m, series.Days[0].Pages);
        Assert.Equal(7m, series.Total);
        Assert.Equal(0, series.LeftOut);
    }

    [Fact]
    public void A_session_just_after_midnight_belongs_to_the_new_day_in_the_readers_zone()
    {
        // 22:30 UTC on the 19th is 01:30 on the 20th in Kyiv (UTC+3): yesterday's reading, not the day before's.
        var kyiv = TimeZoneInfo.CreateCustomTimeZone("kyiv", TimeSpan.FromHours(3), "Kyiv", "Kyiv");
        var lateOnThe19th = new ReadingSessionView(
            Guid.NewGuid(), 9, "Pages", "Reader",
            new DateTimeOffset(new DateTime(2026, 9, 19, 22, 30, 0), TimeSpan.Zero), null, null);

        var series = DailyPages.Of([lateOnThe19th], 300, Today, kyiv);

        Assert.Equal(9m, series.Days.Single(day => day.Day == new DateOnly(2026, 9, 20)).Pages);
        Assert.Equal(0m, series.Days.Single(day => day.Day == new DateOnly(2026, 9, 19)).Pages);
    }
}
