using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// Reading what Library said about how much of a book has been read, once, as a case a display
/// can tell apart. Library does the adding up, the converting and the stop at the end of the
/// book (its own tests); this only has to read the answer the same way wherever it is read.
/// </summary>
public sealed class AmountReadTests
{
    [Fact]
    public void No_progress_is_not_started_which_is_not_the_same_as_none_of_it_read()
    {
        Assert.Equal(new AmountRead.NotStarted(), AmountRead.Of(LibrarySays.Nothing));
    }

    [Fact]
    public void Pages_and_percent_with_no_page_count_need_one()
    {
        Assert.Equal(new AmountRead.NeedsPageCount(), AmountRead.Of(LibrarySays.CannotTotal));
    }

    [Fact]
    public void A_total_in_pages_of_a_book_of_known_length_is_a_share_of_it()
    {
        var read = Assert.IsType<AmountRead.InPages>(AmountRead.Of(LibrarySays.Pages(138, 56)));

        Assert.Equal(138m, read.Amount);
        Assert.Equal(56, read.PercentComplete);
        Assert.Equal(138m, read.PagesRead);
    }

    [Fact]
    public void A_total_in_pages_of_a_book_of_no_known_length_is_not_a_share_of_anything()
    {
        var read = Assert.IsType<AmountRead.InPages>(AmountRead.Of(LibrarySays.Pages(120, null)));

        Assert.Equal(120m, read.Amount);
        Assert.Null(read.PercentComplete);
        Assert.Equal(120m, read.PagesRead);
    }

    [Fact]
    public void A_total_in_percent_says_its_percent_and_is_not_pages()
    {
        var read = Assert.IsType<AmountRead.InPercent>(AmountRead.Of(LibrarySays.Percent(13.5m, 14)));

        Assert.Equal(13.5m, read.Amount);
        Assert.Equal(14, read.PercentComplete);
        Assert.Null(read.PagesRead);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(99, false)]
    public void The_end_is_a_hundred_percent_and_nothing_less(int percent, bool reached)
    {
        // Library never rounds up to 100, so 100 means the whole book.
        Assert.Equal(reached, AmountRead.Of(LibrarySays.Pages(percent * 3, percent)).ReachedTheEnd);
        Assert.Equal(reached, AmountRead.Of(LibrarySays.Percent(percent, percent)).ReachedTheEnd);
    }

    [Fact]
    public void Without_a_page_count_or_a_start_there_is_no_end_to_have_reached()
    {
        Assert.False(AmountRead.Of(LibrarySays.Pages(5000, null)).ReachedTheEnd);
        Assert.False(AmountRead.Of(LibrarySays.CannotTotal).ReachedTheEnd);
        Assert.False(AmountRead.Of(LibrarySays.Nothing).ReachedTheEnd);
    }

    [Fact]
    public void An_entry_reads_its_own_progress_this_way()
    {
        var entry = new LibraryEntry(
            Guid.NewGuid(), Guid.NewGuid(), "Reading", "Pages", DateTimeOffset.UnixEpoch, null, null, 300, null,
            LibrarySays.Pages(150, 50), null);

        Assert.Equal(new AmountRead.InPages(150m, 50), entry.AmountRead);
    }

    // The shapes below are ones Library cannot produce. They are here so that a display never
    // has to decide what they mean: they are read as saying nothing about how far the reader is,
    // never trusted for a figure and never thrown at the page.

    [Theory]
    [MemberData(nameof(ShapesLibraryCannotProduce))]
    public void A_shape_library_cannot_produce_says_nothing_about_how_far_the_reader_is(Progress impossible)
    {
        var read = AmountRead.Of(impossible);

        Assert.Equal(new AmountRead.NeedsPageCount(), read);
        Assert.Null(read.PercentComplete);
        Assert.Null(read.PagesRead);
        Assert.False(read.ReachedTheEnd);
    }

    public static TheoryData<Progress> ShapesLibraryCannotProduce() => new()
    {
        // A percent with no amount to be a percent of.
        new Progress(null, null, 25),
        new Progress(null, "Pages", 25),
        // An amount with no unit to say what it is in.
        new Progress(40, null, 40),
        // A unit nobody sends.
        new Progress(40, "Chapters", 10),
        // A percentage total with no whole percent: the unit is the percent.
        new Progress(40, "Percentage", null),
    };
}
