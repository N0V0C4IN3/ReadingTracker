using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// A year of reading, day by day: sessions placed on the reader's own days, the calendar's
/// weeks, the streaks, the busiest day and the day of the week read most on.
/// </summary>
public class ReadingYearTests
{
    private static readonly Guid Dune = Guid.NewGuid();
    private static readonly Guid Piranesi = Guid.NewGuid();
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly TimeZoneInfo PlusThree = TimeZoneInfo.CreateCustomTimeZone("plus-three", TimeSpan.FromHours(3), "+3", "+3");

    private static SessionInPages Read(Guid entry, string when, decimal? pages, int? minutes = null, bool fromDevice = false) =>
        new(entry, DateTimeOffset.Parse(when), pages, minutes, fromDevice);

    private static ReadingSpan Span(params SessionInPages[] sessions) => new([], sessions);

    private static ReadingYear Year(DateOnly today, params SessionInPages[] sessions) =>
        ReadingYear.From(2026, Span(sessions), today, Utc);

    [Fact]
    public void Adds_up_a_days_sittings_book_by_book()
    {
        var year = Year(new DateOnly(2026, 9, 28),
            Read(Dune, "2026-09-16T08:00:00Z", 20, 30),
            Read(Dune, "2026-09-16T21:00:00Z", 21, 25, fromDevice: true),
            Read(Piranesi, "2026-09-16T22:00:00Z", 23));

        var day = year.Days[new DateOnly(2026, 9, 16)];

        Assert.Equal(64m, day.Pages);
        Assert.Equal(55, day.Minutes);
        Assert.Equal([Dune, Piranesi], day.Books.Select(book => book.EntryId));
        Assert.True(day.Books[0].FromDevice);
        Assert.Equal(4, day.Level);
    }

    [Fact]
    public void Puts_a_session_on_the_day_it_was_read_where_the_reader_is()
    {
        var year = ReadingYear.From(2026, Span(Read(Dune, "2026-03-01T22:30:00Z", 10)), new DateOnly(2026, 9, 28), PlusThree);

        Assert.Equal([new DateOnly(2026, 3, 2)], year.Days.Keys);
    }

    [Fact]
    public void A_day_read_in_unknown_pages_is_still_a_day_read()
    {
        var day = Year(new DateOnly(2026, 9, 28), Read(Dune, "2026-04-04T10:00:00Z", null)).Days.Values.Single();

        Assert.Equal(0m, day.Pages);
        Assert.Null(day.Books.Single().Pages);
        Assert.True(day.SomePagesUnknown);
        Assert.Equal(1, day.Level);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(14.9, 1)]
    [InlineData(15, 2)]
    [InlineData(30, 3)]
    [InlineData(60, 4)]
    public void Colours_a_day_by_how_much_was_read(double pages, int level)
    {
        Assert.Equal(level, Year(new DateOnly(2026, 9, 28), Read(Dune, "2026-04-04T10:00:00Z", (decimal)pages)).Days.Values.Single().Level);
    }

    [Fact]
    public void Counts_the_run_going_now_from_yesterday_until_today_is_read()
    {
        var today = new DateOnly(2026, 9, 28);
        var read = new[] { 24, 25, 26, 27 }.Select(day => Read(Dune, $"2026-09-{day}T10:00:00Z", 10)).ToArray();

        var streak = Year(today, read).Current!;

        Assert.Equal(new DateOnly(2026, 9, 24), streak.From);
        Assert.Equal(4, streak.Days);
    }

    [Fact]
    public void A_day_missed_ends_the_run()
    {
        var year = Year(new DateOnly(2026, 9, 28), Read(Dune, "2026-09-25T10:00:00Z", 10), Read(Dune, "2026-09-26T10:00:00Z", 10));

        Assert.Null(year.Current);
    }

    [Fact]
    public void Finds_the_longest_run_and_the_biggest_day()
    {
        var year = Year(new DateOnly(2026, 9, 28),
            Read(Dune, "2026-05-04T10:00:00Z", 10),
            Read(Dune, "2026-05-05T10:00:00Z", 148),
            Read(Dune, "2026-05-06T10:00:00Z", 10),
            Read(Dune, "2026-06-01T10:00:00Z", 10),
            Read(Dune, "2026-06-02T10:00:00Z", 10));

        Assert.Equal(new Streak(new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 6)), year.Longest);
        Assert.Equal(new DateOnly(2026, 5, 5), year.BiggestDay!.Date);
        Assert.Equal(5, year.DaysRead);
    }

    [Fact]
    public void A_year_gone_by_runs_to_its_last_day_and_has_no_run_going()
    {
        var year = ReadingYear.From(2025, Span(Read(Dune, "2025-12-31T10:00:00Z", 10)), new DateOnly(2026, 1, 1), Utc);

        Assert.Equal(365, year.DaysSoFar);
        Assert.False(year.IsCurrent);
        Assert.Null(year.Current);
    }

    [Fact]
    public void Lays_the_calendar_out_in_weeks_from_monday_with_the_days_outside_the_year_left_blank()
    {
        var year = Year(new DateOnly(2026, 9, 28), Read(Dune, "2026-01-01T10:00:00Z", 20));

        // 1 January 2026 is a Thursday; 31 December a Thursday too.
        Assert.Equal(53, year.Weeks.Count);
        Assert.Equal([null, null, null], year.Weeks[0].Days.Take(3));
        Assert.Equal(new DateOnly(2026, 1, 1), year.Weeks[0].Days[3]!.Date);
        Assert.Equal(20m, year.Weeks[0].Days[3]!.Read!.Pages);
        Assert.Equal(new DateOnly(2026, 12, 31), year.Weeks[^1].Days[3]!.Date);
        Assert.Null(year.Weeks[^1].Days[4]);
    }

    [Fact]
    public void Names_each_month_over_the_week_it_starts_in()
    {
        var weeks = Year(new DateOnly(2026, 9, 28)).Weeks;

        Assert.Equal("Jan", weeks[0].Month);
        Assert.Null(weeks[1].Month);
        // 1 February 2026 is a Sunday, the last day of the fifth week.
        Assert.Equal("Feb", weeks[4].Month);
    }

    [Fact]
    public void Marks_the_weeks_a_phone_shows_and_the_ones_still_to_come()
    {
        // 28 September 2026 is the Monday of the 40th week.
        var weeks = Year(new DateOnly(2026, 9, 28)).Weeks;

        Assert.Equal(ReadingYear.RecentWeeks, weeks.Count(week => week.Recent));
        Assert.True(weeks[39].Recent);
        Assert.False(weeks[39].Ahead);
        Assert.True(weeks[40].Ahead);
        Assert.True(weeks[39].Days[1]!.Ahead);
        Assert.False(weeks[22].Recent);
    }

    [Fact]
    public void Leaves_out_a_day_that_has_not_come_yet()
    {
        // A device with its clock ahead, say.
        var year = Year(new DateOnly(2026, 9, 28), Read(Dune, "2026-09-30T10:00:00Z", 40));

        Assert.Empty(year.Days);
        Assert.Null(year.BiggestDay);
    }

    [Fact]
    public void Opens_on_today_when_read_on_else_the_last_day_read()
    {
        var today = new DateOnly(2026, 9, 28);

        Assert.Equal(today, Year(today, Read(Dune, "2026-09-28T08:00:00Z", 10)).FirstPicked);
        Assert.Equal(new DateOnly(2026, 9, 20), Year(today, Read(Dune, "2026-09-20T08:00:00Z", 10)).FirstPicked);
        Assert.Equal(today, Year(today).FirstPicked);
    }

    [Fact]
    public void Names_a_book_by_its_title_and_one_catalog_could_not_name_as_a_book()
    {
        var year = ReadingYear.From(2026, new ReadingSpan([new(Dune, "Dune"), new(Piranesi, null)], []), new DateOnly(2026, 9, 28), Utc);

        Assert.Equal("Dune", year.TitleOf(Dune));
        Assert.Equal("A book", year.TitleOf(Piranesi));
    }

    [Fact]
    public void Averages_each_weekday_over_every_one_of_it_so_far()
    {
        // Sundays so far to 11 January: the 4th and the 11th. 60 pages on one of them is 30 a Sunday.
        var year = Year(new DateOnly(2026, 1, 11), Read(Dune, "2026-01-04T10:00:00Z", 60));

        Assert.Equal(30m, year.ByWeekday.Single(day => day.Day == DayOfWeek.Sunday).Pages);
        Assert.Equal(0m, year.ByWeekday.Single(day => day.Day == DayOfWeek.Monday).Pages);
    }

    [Fact]
    public void Asks_for_the_year_from_midnight_to_midnight_where_the_reader_is()
    {
        var (from, to) = ReadingYear.Span(2026, PlusThree);

        Assert.Equal(DateTimeOffset.Parse("2025-12-31T21:00:00Z"), from);
        Assert.Equal(DateTimeOffset.Parse("2026-12-31T21:00:00Z"), to);
    }
}
