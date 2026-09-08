namespace ReadingTracker.Library.Entries;

/// <summary>
/// Expresses a position recorded in one TrackingMethod in terms of the other. This is what
/// lets a reader change how they track a book without anything they logged being rewritten:
/// past ReadingSessions keep their own unit and are converted only on the way out.
/// </summary>
public static class UnitConversion
{
    /// <summary>
    /// Converts <paramref name="position"/> from <paramref name="from"/> to <paramref name="to"/>.
    /// Null when the two differ and nobody knows how long the book is: there is no honest
    /// answer, and a made-up one would tell the reader they are somewhere they are not.
    /// </summary>
    public static decimal? Convert(decimal position, TrackingMethod from, TrackingMethod to, int? totalPages)
    {
        if (from == to)
        {
            return position;
        }

        if (totalPages is not > 0)
        {
            return null;
        }

        return to switch
        {
            TrackingMethod.Percentage => position * 100m / totalPages.Value,

            // Rounded, because a reader is on a page rather than four fifths of the way into one.
            _ => Math.Round(position * totalPages.Value / 100m, MidpointRounding.AwayFromZero),
        };
    }
}
