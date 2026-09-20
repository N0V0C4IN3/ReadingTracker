using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public class GoalPaceTests
{
    private static readonly DateOnly Sept20 = new(2026, 9, 20); // day 263 of 365

    [Fact]
    public void Says_how_many_would_be_on_pace_by_today()
    {
        Assert.Equal(18, GoalPace.Of(14, 24, Sept20).OnPaceToday); // 24 × 263/365 = 17.3, rounded up
    }

    [Theory]
    [InlineData(20, "Ahead of pace")]
    [InlineData(18, "On pace")]
    [InlineData(14, "4 behind pace")]
    [InlineData(17, "1 behind pace")]
    [InlineData(24, "Done for the year")]
    [InlineData(30, "Done for the year")]
    public void Says_where_the_reader_stands(int finished, string label)
    {
        Assert.Equal(label, GoalPace.Of(finished, 24, Sept20).Label);
    }

    [Fact]
    public void Counts_a_leap_year_by_its_own_length()
    {
        Assert.Equal(12, GoalPace.Of(0, 24, new DateOnly(2028, 7, 1)).OnPaceToday); // day 183 of 366
    }
}
