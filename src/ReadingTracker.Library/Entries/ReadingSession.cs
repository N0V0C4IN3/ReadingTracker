namespace ReadingTracker.Library.Entries;

/// <summary>
/// A discrete stretch of reading against one LibraryEntry: "read 22 pages on Tuesday evening".
/// The record of when and how much someone read.
///
/// What is recorded is an <see cref="Amount"/> read, not the pages it spanned. A reader knows
/// how much they got through far more readily than which page they stopped on — and it is what
/// they are asked for on paper and in every reading challenge — so it is what they are asked for
/// here. See ADR-0010.
///
/// A session keeps the <see cref="Unit"/> it was logged in for good. When a reader changes their
/// entry's TrackingMethod, past sessions are converted for *display* and never rewritten —
/// otherwise the record of what the reader actually entered is lost.
/// </summary>
public sealed class ReadingSession
{
    public Guid Id { get; init; }

    public Guid LibraryEntryId { get; init; }

    /// <summary>How much was read in this sitting, in <see cref="Unit"/>. Always more than nothing.</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// The unit this session was logged in. A change of TrackingMethod never touches it — that
    /// is a display-time conversion — but correcting the session does, because a correction is
    /// stated afresh in whatever unit the reader is looking at it in.
    /// </summary>
    public TrackingMethod Unit { get; set; }

    /// <summary>When the reading happened, which may be earlier than when it was logged.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    public int? DurationMinutes { get; set; }

    public DateTimeOffset LoggedAt { get; init; }
}

/// <summary>
/// Everything a reader has read of one book, kept apart by the unit it was logged in because
/// pages and percentages cannot be added together without knowing how long the book is.
/// </summary>
public sealed record ReadingTotals(decimal Pages, decimal Percent)
{
    /// <summary>A book with no sessions against it. Not the same as having read none of it.</summary>
    public static ReadingTotals Nothing { get; } = new(0m, 0m);

    public bool Any => Pages > 0m || Percent > 0m;

    public ReadingTotals Plus(decimal amount, TrackingMethod unit) => unit is TrackingMethod.Percentage
        ? this with { Percent = Percent + amount }
        : this with { Pages = Pages + amount };

    /// <summary>
    /// Everything read so far, expressed in one unit. Null when the reader has logged in both
    /// units and nobody knows how long the book is, so there is no bridge between them: a total
    /// that cannot be worked out, which is not the same as a total of nothing.
    /// </summary>
    public decimal? In(TrackingMethod unit, int? totalPages)
    {
        var pages = Express(Pages, TrackingMethod.Pages, unit, totalPages);
        var percent = Express(Percent, TrackingMethod.Percentage, unit, totalPages);

        return pages is { } inPages && percent is { } inPercent ? inPages + inPercent : null;
    }

    /// <summary>
    /// Nothing read in a unit needs no conversion — no pages is no percent whatever the book's
    /// length — so a reader who has only ever logged one unit is never told their total is
    /// unknowable because of the other.
    /// </summary>
    private static decimal? Express(decimal amount, TrackingMethod from, TrackingMethod to, int? totalPages) =>
        amount == 0m ? 0m : UnitConversion.Convert(amount, from, to, totalPages);
}

/// <summary>
/// How much of a book a reader has read. Always the sum of their ReadingSessions rather than
/// stored, so it cannot drift from the history it comes from.
/// </summary>
/// <param name="AmountRead">
/// The total read, in <paramref name="Unit"/>. Null when the reader has logged in both pages and
/// percent and nobody knows how long the book is, so the two cannot be added up.
/// </param>
/// <param name="Unit">The unit the total is expressed in. Null exactly when the total is.</param>
/// <param name="PercentComplete">
/// A whole percent, for display. Null when the book's length is unknown, rather than zero.
/// </param>
public sealed record ReadingProgress(decimal? AmountRead, TrackingMethod? Unit, int? PercentComplete);
