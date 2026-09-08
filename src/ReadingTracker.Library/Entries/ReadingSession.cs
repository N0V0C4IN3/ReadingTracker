namespace ReadingTracker.Library.Entries;

/// <summary>
/// A discrete stretch of reading against one LibraryEntry: "read from page 98 to 120 on
/// Tuesday evening". The record of when and how much someone read.
///
/// A session keeps the <see cref="Unit"/> it was logged in for good. When a reader changes
/// their entry's TrackingMethod, past sessions are converted for *display* and never
/// rewritten — otherwise the record of what the reader actually entered is lost.
/// </summary>
public sealed class ReadingSession
{
    public Guid Id { get; init; }

    public Guid LibraryEntryId { get; init; }

    public decimal StartPosition { get; set; }

    public decimal EndPosition { get; set; }

    /// <summary>The unit this session was logged in, which is never changed after the fact.</summary>
    public TrackingMethod Unit { get; init; }

    /// <summary>When the reading happened, which may be earlier than when it was logged.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    public int? DurationMinutes { get; set; }

    public DateTimeOffset LoggedAt { get; init; }
}

/// <summary>
/// How far through a book a reader is. Always worked out from the most recent ReadingSession
/// rather than stored, so it cannot drift from the history it comes from.
/// </summary>
/// <param name="Position">The position reached, in <paramref name="Unit"/>.</param>
/// <param name="Unit">The unit the position is expressed in.</param>
/// <param name="PercentComplete">
/// A whole percent, for display. Null when the book's length is unknown, rather than zero.
/// </param>
public sealed record ReadingProgress(decimal Position, TrackingMethod Unit, int? PercentComplete);
