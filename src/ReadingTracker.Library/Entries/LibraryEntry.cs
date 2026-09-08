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
    /// The page count everything else is worked out from: this reader's own where they set one,
    /// otherwise whatever Catalog knows — which may still be nothing at all.
    /// </summary>
    public int? EffectivePageCount(int? catalogPageCount) => PageCountOverride ?? catalogPageCount;
}
