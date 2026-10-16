namespace ReadingTracker.Library.Entries;

/// <summary>
/// A passage of a Book the Reader chose to keep, in the Book's own words, sent on purpose from
/// the device they were reading it on (ADR-0017). It belongs to one LibraryEntry, as private as
/// the rest of it, and goes when the entry goes.
///
/// Once sent it is the tracker's: deleting or restyling the highlight on the device leaves it
/// alone, and how the device draws it — colour, style — is never asked for.
/// </summary>
public sealed class Quote
{
    public Guid Id { get; init; }

    public Guid LibraryEntryId { get; init; }

    /// <summary>
    /// The highlight's identity on the device, stable for as long as that highlight exists there.
    /// Opaque to Library, and unique per LibraryEntry: a second save with the same identity is the
    /// same Quote, so a retried request can never make a duplicate. Matching by the passage's text
    /// was rejected — it would merge a re-read's highlight of the same words into the first.
    /// </summary>
    public required string HighlightId { get; init; }

    /// <summary>The Book's words. Read-only once saved: nothing the Reader or a device does changes them.</summary>
    public required string Passage { get; init; }

    /// <summary>The Reader's own words on the passage. Null when they wrote none.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Where the passage starts, as a percentage of the Book — the same measure as the Bookmark,
    /// so Quotes fall into book order whatever the font size or edition.
    /// </summary>
    public decimal Percent { get; init; }

    /// <summary>The chapter the passage is in, when the device knows it.</summary>
    public string? Chapter { get; init; }

    /// <summary>When the Reader saved it, by the device's account — which, on a device that syncs later, is not when it arrived.</summary>
    public DateTimeOffset SavedAt { get; init; }
}

/// <summary>Whether a device's save made a new Quote or found the one it had already sent.</summary>
public enum QuoteSaveOutcome
{
    Created,
    Updated,
}

/// <summary>Why a Quote could not be saved or changed as asked.</summary>
public enum QuoteProblem
{
    /// <summary>The reader has no such entry — including because it is someone else's.</summary>
    NoSuchEntry,

    /// <summary>The entry has no such Quote — including because it was already deleted.</summary>
    NoSuchQuote,
}
