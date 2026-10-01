using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public class StopRangeTests
{
    private static AmountRead Read(Progress? progress) => AmountRead.Of(progress);

    [Fact]
    public void A_book_in_pages_runs_from_the_pages_read_to_its_page_count()
    {
        var range = StopRange.For("Pages", 248, Read(LibrarySays.Pages(138, 56)))!;

        Assert.Equal(new StopRange(true, 248, 138), range);
        Assert.Equal(110, range.Left);
    }

    [Fact]
    public void A_book_in_percent_runs_from_its_percent_to_a_hundred()
    {
        var range = StopRange.For("Percentage", 248, Read(LibrarySays.Percent(56, 56)))!;

        Assert.Equal(new StopRange(false, 100, 56), range);
    }

    [Fact]
    public void A_book_in_percent_needs_no_page_count()
    {
        Assert.Equal(new StopRange(false, 100, 30), StopRange.For("Percentage", null, Read(LibrarySays.Percent(30, 30))));
    }

    [Fact]
    public void A_book_not_started_starts_at_nothing()
    {
        Assert.Equal(new StopRange(true, 300, 0), StopRange.For("Pages", 300, Read(LibrarySays.Nothing)));
    }

    [Fact]
    public void A_book_in_percent_with_no_page_count_and_pages_logged_starts_at_nothing()
    {
        // Pages logged, nobody knows the length, and the reader now tracks by percent: Library
        // says the total in the unit it was logged in, which is not a place on a percent slider.
        Assert.Equal(new StopRange(false, 100, 0), StopRange.For("Percentage", null, Read(LibrarySays.Pages(120, null))));
    }

    [Fact]
    public void A_total_library_cannot_total_places_nobody_and_starts_at_nothing()
    {
        // Logged in both units with no page count: there is no page count to make a range of in
        // pages, and in percent there is no total to say where the reader is.
        Assert.Null(StopRange.For("Pages", null, Read(LibrarySays.CannotTotal)));
        Assert.Equal(new StopRange(false, 100, 0), StopRange.For("Percentage", null, Read(LibrarySays.CannotTotal)));
    }

    [Fact]
    public void A_shape_library_cannot_produce_is_not_trusted_to_place_the_reader()
    {
        // A percent and no amount cannot be what Library says; the slider starts at the top
        // rather than somewhere worked out from half of an answer.
        Assert.Equal(new StopRange(true, 200, 0), StopRange.For("Pages", 200, AmountRead.Of(new Progress(null, null, 25))));
    }

    [Fact]
    public void A_book_in_pages_with_no_page_count_has_no_range() =>
        Assert.Null(StopRange.For("Pages", null, Read(LibrarySays.Pages(40, null))));

    [Theory]
    [InlineData(248)]
    [InlineData(260)]
    public void A_book_read_to_its_end_or_past_it_has_no_range(int read) =>
        Assert.Null(StopRange.For("Pages", 248, Read(LibrarySays.Pages(read, 100))));

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
