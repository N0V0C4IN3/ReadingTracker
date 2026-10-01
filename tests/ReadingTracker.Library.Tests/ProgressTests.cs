using ReadingTracker.Library.Entries;

namespace ReadingTracker.Library.Tests;

/// <summary>
/// How much of a book a reader has read, worked out from the totals of their sessions: the unit
/// it is said in, the stop at the whole book, and the whole percent shown beside it. Pure, so
/// these run without a database; the HTTP tests elsewhere only prove the answer reaches the wire.
/// </summary>
public sealed class ProgressTests
{
    private static ReadingTotals Pages(decimal pages) => new(pages, 0m);

    private static ReadingTotals Percent(decimal percent) => new(0m, percent);

    [Fact]
    public void A_book_with_no_sessions_has_no_amount_read_at_all()
    {
        // Not zero: nothing logged is different from none of it read.
        Assert.Null(Progress.Of(ReadingTotals.Nothing, TrackingMethod.Pages, 300));
    }

    [Fact]
    public void Pages_logged_in_a_book_tracked_by_pages_are_their_own_total_and_a_share_of_the_book()
    {
        var read = Progress.Of(Pages(150), TrackingMethod.Pages, 300);

        Assert.Equal(new ReadingProgress(150m, TrackingMethod.Pages, 50), read);
    }

    [Fact]
    public void Pages_logged_are_said_as_a_percentage_once_the_reader_tracks_by_percentage()
    {
        var read = Progress.Of(Pages(150), TrackingMethod.Percentage, 300);

        Assert.Equal(new ReadingProgress(50m, TrackingMethod.Percentage, 50), read);
    }

    [Fact]
    public void A_percentage_logged_is_said_in_pages_once_the_reader_tracks_by_pages()
    {
        var read = Progress.Of(Percent(25), TrackingMethod.Pages, 300);

        Assert.Equal(new ReadingProgress(75m, TrackingMethod.Pages, 25), read);
    }

    [Fact]
    public void Pages_and_percentages_are_added_up_once_the_length_bridges_them()
    {
        // 60 pages is 20% of 300, and 25% more has been read since.
        var read = Progress.Of(new ReadingTotals(60m, 25m), TrackingMethod.Percentage, 300);

        Assert.Equal(new ReadingProgress(45m, TrackingMethod.Percentage, 45), read);
    }

    [Fact]
    public void Both_units_with_no_length_to_bridge_them_say_nothing_rather_than_a_smaller_number()
    {
        var read = Progress.Of(new ReadingTotals(120m, 20m), TrackingMethod.Percentage, null);

        Assert.Equal(new ReadingProgress(null, null, null), read);
    }

    [Fact]
    public void Without_a_length_the_total_is_said_in_the_unit_it_was_logged_and_no_percent_is_made_up()
    {
        // Tracked by percentage, but only pages were logged and nobody knows the length.
        var read = Progress.Of(Pages(120), TrackingMethod.Percentage, null);

        Assert.Equal(new ReadingProgress(120m, TrackingMethod.Pages, null), read);
    }

    [Fact]
    public void Without_a_length_a_percentage_logged_stays_a_percentage()
    {
        var read = Progress.Of(Percent(35), TrackingMethod.Percentage, null);

        Assert.Equal(new ReadingProgress(35m, TrackingMethod.Percentage, 35), read);
    }

    [Fact]
    public void A_zero_page_length_is_no_length()
    {
        var read = Progress.Of(Pages(10), TrackingMethod.Pages, 0);

        Assert.Equal(new ReadingProgress(10m, TrackingMethod.Pages, null), read);
    }

    [Theory]
    [InlineData(320, 300)]
    [InlineData(301, 300)]
    public void The_total_stops_at_the_last_page_however_much_was_logged(int logged, int length)
    {
        var read = Progress.Of(Pages(logged), TrackingMethod.Pages, length);

        Assert.Equal(new ReadingProgress(length, TrackingMethod.Pages, 100), read);
    }

    [Fact]
    public void The_total_stops_at_all_of_the_book_when_a_percentage_runs_past_it()
    {
        var read = Progress.Of(Percent(140), TrackingMethod.Percentage, 300);

        Assert.Equal(new ReadingProgress(100m, TrackingMethod.Percentage, 100), read);
    }

    [Fact]
    public void A_total_past_the_end_converted_to_the_other_unit_still_stops_at_the_end()
    {
        // 140% of a 300-page book, said in pages: the book has 300 of them.
        var read = Progress.Of(Percent(140), TrackingMethod.Pages, 300);

        Assert.Equal(new ReadingProgress(300m, TrackingMethod.Pages, 100), read);
    }

    [Fact]
    public void Nothing_stops_a_total_when_nobody_knows_how_long_the_book_is()
    {
        var read = Progress.Of(Pages(5000), TrackingMethod.Pages, null);

        Assert.Equal(new ReadingProgress(5000m, TrackingMethod.Pages, null), read);
    }

    [Fact]
    public void A_page_still_to_go_is_never_a_hundred_percent()
    {
        // 100 means the whole book and nothing less: it is what lets a client ask "have you
        // finished?" without ever asking someone with a chapter left.
        var read = Progress.Of(Pages(299), TrackingMethod.Pages, 300);

        Assert.Equal(99, read!.PercentComplete);
    }

    [Fact]
    public void A_little_of_a_long_book_is_never_nought_percent()
    {
        var read = Progress.Of(Pages(1), TrackingMethod.Pages, 1000);

        Assert.Equal(1, read!.PercentComplete);
    }

    [Fact]
    public void The_whole_book_is_a_hundred_percent()
    {
        var read = Progress.Of(Pages(300), TrackingMethod.Pages, 300);

        Assert.Equal(100, read!.PercentComplete);
    }

    [Theory]
    [InlineData(149, 50)]
    [InlineData(150, 50)]
    [InlineData(151, 50)]
    [InlineData(148, 49)]
    public void The_percent_is_rounded_to_a_whole_one_half_away_from_nought(int pages, int percent)
    {
        // 148 of 300 is 49.33, 149 is 49.67, 150 is exactly 50, 151 is 50.33.
        var read = Progress.Of(Pages(pages), TrackingMethod.Pages, 300);

        Assert.Equal(percent, read!.PercentComplete);
    }

    [Fact]
    public void A_percentage_typed_by_the_reader_keeps_its_own_precision_in_the_total()
    {
        var read = Progress.Of(Percent(17.5m), TrackingMethod.Percentage, 300);

        Assert.Equal(17.5m, read!.AmountRead);
        Assert.Equal(18, read.PercentComplete);
    }

    [Fact]
    public void As_a_percentage_the_amount_read_is_the_percentage_it_adds_up_to()
    {
        Assert.Equal(45m, Progress.AsPercent(new ReadingTotals(60m, 25m), 300));
    }

    [Fact]
    public void As_a_percentage_the_amount_read_never_passes_the_whole_book()
    {
        Assert.Equal(100m, Progress.AsPercent(Percent(140), 300));
        Assert.Equal(100m, Progress.AsPercent(Pages(320), 300));
    }

    [Fact]
    public void As_a_percentage_pages_with_no_length_cannot_be_said()
    {
        Assert.Null(Progress.AsPercent(Pages(120), null));
    }

    [Fact]
    public void As_a_percentage_a_percentage_needs_no_length()
    {
        Assert.Equal(35m, Progress.AsPercent(Percent(35), null));
    }

    [Fact]
    public void As_a_percentage_nothing_read_is_nought()
    {
        Assert.Equal(0m, Progress.AsPercent(ReadingTotals.Nothing, 300));
    }
}
