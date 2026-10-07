using System.Globalization;

namespace ReadingTracker.Web.Services;

/// <summary>
/// What exporting the shelf comes to: the whole shelf as one file in Goodreads' layout
/// (<see cref="GoodreadsCsv"/>), or the reason there is none. Never half a shelf: a reader who
/// moves their books elsewhere must not take a file missing some of them for all of them.
/// </summary>
public abstract record ShelfFile
{
    private ShelfFile()
    {
    }

    /// <summary>
    /// Reads the shelf as <see cref="GatewayLibraryClient.GetEntriesAsync"/> gave it. Library
    /// sends every entry even when Catalog cannot describe them, just without their books, so one
    /// entry with no book is a shelf that did not load whole. The file is named for the reader's
    /// own day.
    /// </summary>
    public static ShelfFile Of(
        IReadOnlyList<LibraryEntry>? entries,
        LibraryUnavailable? problem,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        if (problem is LibraryUnavailable.NotSignedIn)
        {
            return new SignedOut();
        }

        if (problem is not null || entries is null)
        {
            return new NotLoaded();
        }

        if (entries.Count == 0)
        {
            return new Empty();
        }

        var described = entries
            .Where(entry => entry.Book is not null)
            .Select(entry => (entry, entry.Book!))
            .ToList();

        if (described.Count < entries.Count)
        {
            return new NotLoaded();
        }

        var day = ReaderDays.Of(now, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new Ready($"readingtracker-library-{day}.csv", GoodreadsCsv.Write(described, zone));
    }

    /// <summary>The whole shelf, ready to save.</summary>
    public sealed record Ready(string Name, string Text) : ShelfFile;

    /// <summary>Nothing on the shelf: no file of only a header.</summary>
    public sealed record Empty : ShelfFile;

    /// <summary>The shelf, or some of its books, could not be had just now.</summary>
    public sealed record NotLoaded : ShelfFile;

    /// <summary>The reader's session has ended.</summary>
    public sealed record SignedOut : ShelfFile;
}
