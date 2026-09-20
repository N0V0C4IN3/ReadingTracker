using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public class ShelfOrderTests
{
    private static LibraryEntry Entry(string status, int addedDaysAgo, DateOnly? finishedOn = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), status, "Pages", DateTimeOffset.UtcNow.AddDays(-addedDaysAgo), finishedOn, null, null, null, null, null);

    [Fact]
    public void Groups_by_status_reading_first_and_dropped_last()
    {
        var dropped = Entry("Dropped", 1);
        var finished = Entry("Finished", 2);
        var reading = Entry("Reading", 3);
        var onHold = Entry("OnHold", 4);
        var wantToRead = Entry("WantToRead", 5);

        var ordered = ShelfOrder.ByStatus([dropped, finished, reading, onHold, wantToRead]);

        Assert.Equal([reading, wantToRead, finished, onHold, dropped], ordered);
    }

    [Fact]
    public void Keeps_newest_first_within_a_group()
    {
        var finished = Entry("Finished", 1);
        var readingNew = Entry("Reading", 2);
        var readingOld = Entry("Reading", 4);

        var ordered = ShelfOrder.ByStatus([finished, readingNew, readingOld]);

        Assert.Equal([readingNew, readingOld, finished], ordered);
    }

    [Fact]
    public void Puts_the_most_recently_finished_book_first_among_the_finished()
    {
        var finishedInSpring = Entry("Finished", 1, new DateOnly(2026, 4, 2));
        var finishedLastWeek = Entry("Finished", 30, new DateOnly(2026, 9, 13));
        var finishedYesterday = Entry("Finished", 10, new DateOnly(2026, 9, 19));

        var ordered = ShelfOrder.ByStatus([finishedInSpring, finishedLastWeek, finishedYesterday]);

        Assert.Equal([finishedYesterday, finishedLastWeek, finishedInSpring], ordered);
    }

    [Fact]
    public void Puts_finished_books_without_a_date_after_those_with_one()
    {
        var undatedNew = Entry("Finished", 1);
        var undatedOld = Entry("Finished", 3);
        var dated = Entry("Finished", 2, new DateOnly(2025, 1, 1));

        var ordered = ShelfOrder.ByStatus([undatedNew, dated, undatedOld]);

        Assert.Equal([dated, undatedNew, undatedOld], ordered);
    }

    [Fact]
    public void Puts_a_status_it_does_not_know_after_the_rest()
    {
        var odd = Entry("Skimming", 1);
        var dropped = Entry("Dropped", 2);

        Assert.Equal([dropped, odd], ShelfOrder.ByStatus([odd, dropped]));
    }
}
