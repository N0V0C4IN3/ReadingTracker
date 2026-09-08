namespace ReadingTracker.Library.Entries;

public static class Progress
{
    /// <summary>
    /// Works out how far through a book a reader is from their most recent session.
    /// Returns null when they have not read anything yet — no sessions means no position,
    /// which is different from being at position zero.
    /// </summary>
    public static ReadingProgress? Of(ReadingSession? latest, TrackingMethod method, int? totalPages)
    {
        if (latest is null)
        {
            return null;
        }

        // Shown in the method the reader now tracks by where that can be worked out, and
        // otherwise in the unit they actually logged — never in one we had to guess at.
        var (position, unit) = UnitConversion.Convert(latest.EndPosition, latest.Unit, method, totalPages) is { } converted
            ? (converted, method)
            : (latest.EndPosition, latest.Unit);

        return new ReadingProgress(position, unit, PercentComplete(position, unit, totalPages));
    }

    /// <summary>
    /// Null rather than zero when the book's length is unknown: "we don't know how far along
    /// you are" and "you are at the beginning" are different answers, and a progress bar
    /// should be able to tell them apart.
    /// </summary>
    private static int? PercentComplete(decimal position, TrackingMethod unit, int? totalPages) =>
        unit switch
        {
            TrackingMethod.Percentage => WholePercent(position),
            _ when totalPages is > 0 => WholePercent(position * 100m / totalPages.Value),
            _ => null,
        };

    /// <summary>
    /// A whole percent, because nobody reads a book to two decimal places. Rounding is held
    /// off the two ends it would misreport: a reader with a page still to go is never shown
    /// 100%, and one who has read a little of a long book is never shown 0%.
    /// </summary>
    private static int WholePercent(decimal exact) => exact switch
    {
        <= 0m => 0,
        >= 100m => 100,
        _ => Math.Clamp((int)Math.Round(exact, MidpointRounding.AwayFromZero), 1, 99),
    };
}
