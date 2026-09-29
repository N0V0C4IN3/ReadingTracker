namespace ReadingTracker.Web.Services;

/// <summary>
/// The ReadingStatus values Library accepts, and the words readers see for them. One table, so a
/// status is called the same thing on the card's menu, the shelf's tabs, an import's list and a
/// search result's sheet.
/// </summary>
public static class ReadingStatuses
{
    /// <summary>Where adding a book puts it: Library adds every new entry as Want to read.</summary>
    public const string OnAdding = "WantToRead";

    /// <summary>Every status, in the order a menu of them reads: not begun, in hand, done, set aside.</summary>
    public static readonly IReadOnlyList<string> All = ["WantToRead", "Reading", "Finished", "OnHold", "Dropped"];

    /// <summary>The statuses a book can go onto the shelf as. A book is not added to be set aside.</summary>
    public static readonly IReadOnlyList<string> AddableAs = ["WantToRead", "Reading", "Finished"];

    /// <summary>
    /// Wanted and not begun: nothing logged and no device has said where the reader is. A book
    /// like that has no progress to show, and what its first log does is start it — Library moves
    /// a wanted book to Reading on its first session. A wanted book that does have reading (put
    /// back on the wish list after some) is not this: that is a fact about the reader they would
    /// lose.
    /// </summary>
    public static bool NotBegun(LibraryEntry entry) =>
        entry.Status == OnAdding && entry.Progress is null && entry.Bookmark is null;

    public static string Label(string status) => status switch
    {
        "WantToRead" => "Want to read",
        "OnHold" => "On hold",
        _ => status,
    };
}
