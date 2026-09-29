using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>How the page writes pages, days and time spent.</summary>
public class FiguresTests
{
    [Theory]
    [InlineData(0.3, "under a page")]
    [InlineData(0, "0 pages")]
    [InlineData(1, "1 page")]
    [InlineData(1.4, "1 page")]
    [InlineData(12.5, "13 pages")]
    public void Writes_pages_to_the_nearest_whole_one(double pages, string said)
    {
        Assert.Equal(said, Figures.Pages((decimal)pages));
    }

    [Theory]
    [InlineData(1, "1 day")]
    [InlineData(4, "4 days")]
    public void Writes_days(int days, string said)
    {
        Assert.Equal(said, Figures.Days(days));
    }

    [Theory]
    [InlineData(45, "45 min")]
    [InlineData(120, "2 h")]
    [InlineData(80, "1 h 20 min")]
    public void Writes_time_spent(int minutes, string said)
    {
        Assert.Equal(said, Figures.Duration(minutes));
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "yesterday")]
    [InlineData(5, "5 days ago")]
    [InlineData(13, "13 days ago")]
    [InlineData(14, "2 weeks ago")]
    [InlineData(21, "3 weeks ago")]
    [InlineData(41, "5 weeks ago")]
    [InlineData(42, "1 month ago")]
    [InlineData(59, "1 month ago")]
    [InlineData(60, "2 months ago")]
    [InlineData(364, "11 months ago")]
    [InlineData(365, "1 year ago")]
    [InlineData(800, "2 years ago")]
    public void Writes_how_long_ago(int days, string said)
    {
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(said, Figures.Ago(now.AddDays(-days), now));
    }

    [Fact]
    public void Never_writes_a_time_in_the_future()
    {
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("today", Figures.Ago(now.AddDays(2), now));
    }

    [Theory]
    [InlineData(387, 21, "387 pages · added 3 weeks ago")]
    [InlineData(1, 0, "1 page · added today")]
    [InlineData(null, 2, "Added 2 days ago")]
    [InlineData(0, 2, "Added 2 days ago")]
    public void Says_what_a_book_not_yet_begun_has(int? pages, int daysAgo, string said)
    {
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(said, Figures.Added(pages, now.AddDays(-daysAgo), now));
    }
}
