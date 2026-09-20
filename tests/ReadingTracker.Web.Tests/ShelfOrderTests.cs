using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public class ShelfOrderTests
{
    private static LibraryEntry Entry(string status, int addedDaysAgo) =>
        new(Guid.NewGuid(), Guid.NewGuid(), status, "Pages", DateTimeOffset.UtcNow.AddDays(-addedDaysAgo), null, null, null, null, null, null);

    [Fact]
    public void Puts_the_books_being_read_first_and_keeps_everything_else_in_its_order()
    {
        var finished = Entry("Finished", 1);
        var readingNew = Entry("Reading", 2);
        var onHold = Entry("OnHold", 3);
        var readingOld = Entry("Reading", 4);
        var wantToRead = Entry("WantToRead", 5);

        var ordered = ShelfOrder.ReadingFirst([finished, readingNew, onHold, readingOld, wantToRead]);

        Assert.Equal([readingNew, readingOld, finished, onHold, wantToRead], ordered);
    }

    [Fact]
    public void Leaves_a_shelf_with_nothing_being_read_as_it_was()
    {
        var entries = new[] { Entry("Finished", 1), Entry("OnHold", 2) };

        Assert.Equal(entries, ShelfOrder.ReadingFirst(entries));
    }
}
