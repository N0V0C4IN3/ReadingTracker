using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>Which wanted books the shelf treats as not begun: no figure or bar, and Start reading.</summary>
public class NotBegunTests
{
    private static LibraryEntry Entry(string status, Progress? progress = null, Bookmark? bookmark = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), status, "Pages", DateTimeOffset.UnixEpoch, null, null, null, null, progress, bookmark);

    [Fact]
    public void A_wanted_book_with_nothing_logged_is_not_begun()
    {
        Assert.True(ReadingStatuses.NotBegun(Entry("WantToRead")));
    }

    [Fact]
    public void A_wanted_book_with_reading_has_begun()
    {
        Assert.False(ReadingStatuses.NotBegun(Entry("WantToRead", new Progress(30, "Pages", 10))));
    }

    [Fact]
    public void A_wanted_book_a_device_has_reported_on_has_begun()
    {
        Assert.False(ReadingStatuses.NotBegun(Entry("WantToRead", bookmark: new Bookmark(12, DateTimeOffset.UnixEpoch))));
    }

    [Theory]
    [InlineData("Reading")]
    [InlineData("Finished")]
    [InlineData("OnHold")]
    [InlineData("Dropped")]
    public void A_book_that_is_not_wanted_is_never_not_begun(string status)
    {
        Assert.False(ReadingStatuses.NotBegun(Entry(status)));
    }
}
