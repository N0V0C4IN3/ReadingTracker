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
}
