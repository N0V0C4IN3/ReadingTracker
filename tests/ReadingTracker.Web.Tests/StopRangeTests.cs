using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public class StopRangeTests
{
    [Fact]
    public void A_book_in_pages_runs_from_the_pages_read_to_its_page_count()
    {
        var range = StopRange.For("Pages", 248, new Progress(138, "Pages", 56))!;

        Assert.Equal(new StopRange(true, 248, 138), range);
        Assert.Equal(110, range.Left);
    }

    [Fact]
    public void A_book_in_percent_runs_from_its_percent_to_a_hundred()
    {
        var range = StopRange.For("Percentage", 248, new Progress(56, "Percentage", 56))!;

        Assert.Equal(new StopRange(false, 100, 56), range);
    }

    [Fact]
    public void A_book_in_percent_needs_no_page_count()
    {
        Assert.Equal(new StopRange(false, 100, 30), StopRange.For("Percentage", null, new Progress(30, "Percentage", 30)));
    }

    [Fact]
    public void A_book_not_started_starts_at_nothing()
    {
        Assert.Equal(new StopRange(true, 300, 0), StopRange.For("Pages", 300, null));
    }

    [Fact]
    public void Sessions_logged_in_both_units_are_placed_by_the_percent_read()
    {
        // No amount, since the two units cannot be added without a page count at the time; the
        // percent says where the reader is regardless.
        Assert.Equal(new StopRange(true, 200, 50), StopRange.For("Pages", 200, new Progress(null, null, 25)));
    }

    [Fact]
    public void A_book_in_pages_with_no_page_count_has_no_range() =>
        Assert.Null(StopRange.For("Pages", null, new Progress(40, "Pages", null)));

    [Theory]
    [InlineData(248)]
    [InlineData(260)]
    public void A_book_read_to_its_end_or_past_it_has_no_range(int read) =>
        Assert.Null(StopRange.For("Pages", 248, new Progress(read, "Pages", 100)));

    [Theory]
    [InlineData(100, 138)]
    [InlineData(138, 138)]
    [InlineData(180, 180)]
    [InlineData(300, 248)]
    public void A_stop_is_held_between_where_the_reader_was_and_the_end(int stop, int held) =>
        Assert.Equal(held, new StopRange(true, 248, 138).Clamp(stop));

    [Fact]
    public void The_amount_is_where_they_stopped_less_where_they_were()
    {
        var range = new StopRange(true, 248, 138);

        Assert.Equal(42, range.AmountTo(180));
        Assert.Equal(0, range.AmountTo(120));
        Assert.Equal(180, range.StopFor(42));
        Assert.Equal(138, range.StopFor(null));
        Assert.Equal(248, range.StopFor(500));
    }

    [Theory]
    [InlineData(true, 180, "Page 180", "+42 pages")]
    [InlineData(true, 139, "Page 139", "+1 page")]
    [InlineData(true, 138, "Page 138", "+0 pages")]
    [InlineData(false, 68, "68%", "+12%")]
    public void Says_where_and_how_much(bool pages, int stop, string place, string gain)
    {
        var range = pages ? new StopRange(true, 248, 138) : new StopRange(false, 100, 56);

        Assert.Equal(place, range.Place(stop));
        Assert.Equal(gain, range.Gain(stop));
    }
}
