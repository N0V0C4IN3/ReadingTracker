namespace ReadingTracker.Library.Entries;

public static class Progress
{
    /// <summary>
    /// Works out how far through a book a reader is from their most recent session.
    /// Returns null when they have not read anything yet — no sessions means no position,
    /// which is different from being at position zero.
    /// </summary>
    public static ReadingProgress? Of(ReadingSession? latest, int? totalPages)
    {
        if (latest is null)
        {
            return null;
        }

        return new ReadingProgress(latest.EndPosition, latest.Unit, PercentComplete(latest, totalPages));
    }

    /// <summary>
    /// Null rather than zero when the book's length is unknown: "we don't know how far along
    /// you are" and "you are at the beginning" are different answers, and a progress bar
    /// should be able to tell them apart.
    /// </summary>
    private static decimal? PercentComplete(ReadingSession latest, int? totalPages) =>
        latest.Unit switch
        {
            TrackingMethod.Percentage => latest.EndPosition,
            _ when totalPages is > 0 => Math.Round(latest.EndPosition * 100m / totalPages.Value, 2),
            _ => null,
        };
}
