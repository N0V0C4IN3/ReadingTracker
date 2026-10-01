using ReadingTracker.Library.Entries;

namespace ReadingTracker.Library.Tests;

/// <summary>Everything a reader has read of one book, kept apart by the unit it was logged in.</summary>
public sealed class ReadingTotalsTests
{
    [Fact]
    public void A_book_with_no_sessions_has_no_totals()
    {
        Assert.False(ReadingTotals.Nothing.Any);
    }

    [Fact]
    public void Each_session_is_added_to_the_unit_it_was_logged_in()
    {
        var totals = ReadingTotals.Nothing
            .Plus(20m, TrackingMethod.Pages)
            .Plus(10m, TrackingMethod.Percentage)
            .Plus(5m, TrackingMethod.Pages);

        Assert.Equal(new ReadingTotals(25m, 10m), totals);
        Assert.True(totals.Any);
    }

    [Fact]
    public void Only_ever_one_unit_logged_is_never_unknowable_because_of_the_other()
    {
        // No pages is no percent whatever the book's length, so nothing needs bridging.
        Assert.Equal(120m, new ReadingTotals(120m, 0m).In(TrackingMethod.Pages, null));
        Assert.Equal(20m, new ReadingTotals(0m, 20m).In(TrackingMethod.Percentage, null));
    }

    [Fact]
    public void Both_units_with_no_length_cannot_be_put_in_one()
    {
        Assert.Null(new ReadingTotals(120m, 20m).In(TrackingMethod.Pages, null));
        Assert.Null(new ReadingTotals(120m, 20m).In(TrackingMethod.Percentage, null));
    }

    [Fact]
    public void Both_units_are_put_in_one_once_the_length_is_known()
    {
        // 60 pages of 300 is 20%, plus 25%; and 25% of 300 is 75 pages, plus 60.
        Assert.Equal(45m, new ReadingTotals(60m, 25m).In(TrackingMethod.Percentage, 300));
        Assert.Equal(135m, new ReadingTotals(60m, 25m).In(TrackingMethod.Pages, 300));
    }
}
