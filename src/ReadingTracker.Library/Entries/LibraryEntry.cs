namespace ReadingTracker.Library.Entries;

/// <summary>The state of a book in one reader's library.</summary>
public enum ReadingStatus
{
    WantToRead,
    Reading,
    Finished,
    OnHold,
    Dropped,
}

/// <summary>How a reader's progress through a book is expressed.</summary>
public enum TrackingMethod
{
    Pages,
    Percentage,
}

/// <summary>
/// The association between a reader and a Book: "this book is in this reader's library".
/// Distinct from the Book itself, which is shared catalog data owned by Catalog, and from a
/// ReadingSession, which records activity.
/// </summary>
public sealed class LibraryEntry
{
    public Guid Id { get; init; }

    /// <summary>Whose library this is. Supplied by the Gateway (ADR-0007).</summary>
    public required string ReaderId { get; init; }

    /// <summary>Identifies a Book in Catalog. Library stores no copy of the book's details.</summary>
    public Guid BookId { get; init; }

    public ReadingStatus Status { get; set; }

    public TrackingMethod TrackingMethod { get; set; }

    /// <summary>
    /// This reader's own page count, where Catalog's is wrong for their edition or missing
    /// altogether. It lives here rather than on the Book because it is one reader's correction
    /// and must never change what every other reader sees.
    /// </summary>
    public int? PageCountOverride { get; set; }

    public DateTimeOffset AddedAt { get; init; }

    /// <summary>
    /// Where this reader currently is in the book, as a percentage, as last reported by a device
    /// — the "where", where sessions are the "how much". Null until a device has said. It is a
    /// report of a position and never a source of truth for how much has been read: moving it
    /// forward is what a device's reading turns into sessions, moving it back records nothing,
    /// and correcting sessions leaves it alone (ADR-0015).
    /// </summary>
    public decimal? BookmarkPercent { get; set; }

    /// <summary>When the reading that put the Bookmark where it is happened, by the device's account.</summary>
    public DateTimeOffset? BookmarkReportedAt { get; set; }

    /// <summary>
    /// The page count everything else is worked out from: this reader's own where they set one,
    /// otherwise whatever Catalog knows — which may still be nothing at all. A zero from Catalog
    /// is nothing at all: providers send 0 for "we don't know", and taken as a length it would
    /// cap every session at zero pages and call the book read.
    /// </summary>
    public int? EffectivePageCount(int? catalogPageCount) =>
        PageCountOverride ?? (catalogPageCount is > 0 ? catalogPageCount : null);
}
