namespace ReadingTracker.Library.Entries;

public static class Progress
{
    /// <summary>
    /// Works out how much of a book a reader has read by adding up their sessions. Returns null
    /// when they have not read anything yet — no sessions means no total, which is different
    /// from having read none of it.
    /// </summary>
    public static ReadingProgress? Of(ReadingTotals totals, TrackingMethod method, int? totalPages)
    {
        if (!totals.Any)
        {
            return null;
        }

        // Shown in the method the reader now tracks by where that can be worked out...
        if (totals.In(method, totalPages) is { } inTheirMethod)
        {
            return Reached(inTheirMethod, method, totalPages);
        }

        // ...and otherwise in the unit they actually logged, never in one we had to guess at. Only
        // one unit can be in play here: a total in both is what needed the page count in the first
        // place.
        var logged = totals.Pages > 0m ? TrackingMethod.Pages : TrackingMethod.Percentage;

        if (totals.In(logged, totalPages) is { } asLogged)
        {
            return Reached(asLogged, logged, totalPages);
        }

        // Pages and percentages both logged, and no page count to add them up with. Saying "you
        // have read 40" of a total that is really 40 pages *and* 20% would be a smaller number
        // than the reader's own reading, which is worse than admitting we cannot say.
        return new ReadingProgress(null, null, null);
    }

    /// <summary>
    /// How far through the book a total puts the reader, which is never further than the end of
    /// it. A reader twenty pages from the end who logs forty has not read 320 pages of a
    /// 300-page book; they have read all of it, and probably want to be asked whether they have
    /// finished. What they typed stays on the record either way — only this derived total stops.
    ///
    /// Nothing stops when nobody knows how long the book is, because there is no end to stop at.
    /// </summary>
    private static ReadingProgress Reached(decimal amountRead, TrackingMethod unit, int? totalPages)
    {
        decimal? wholeBook = unit switch
        {
            TrackingMethod.Percentage => 100m,
            _ => totalPages,
        };

        var read = wholeBook is { } whole && amountRead > whole ? whole : amountRead;

        return new ReadingProgress(read, unit, PercentComplete(read, unit, totalPages));
    }

    /// <summary>
    /// Null rather than zero when the book's length is unknown: "we don't know how far along
    /// you are" and "you are at the beginning" are different answers, and a progress bar
    /// should be able to tell them apart.
    /// </summary>
    private static int? PercentComplete(decimal amountRead, TrackingMethod unit, int? totalPages) =>
        unit switch
        {
            TrackingMethod.Percentage => WholePercent(amountRead),
            _ when totalPages is > 0 => WholePercent(amountRead * 100m / totalPages.Value),
            _ => null,
        };

    /// <summary>
    /// A whole percent, because nobody reads a book to two decimal places. Rounding is held
    /// off the two ends it would misreport: a reader with a page still to go is never shown
    /// 100%, and one who has read a little of a long book is never shown 0%.
    ///
    /// So 100 means the whole book and nothing less, which is what lets a client ask "have you
    /// finished?" on the strength of it without ever asking someone who has a chapter to go.
    /// </summary>
    private static int WholePercent(decimal exact) => exact switch
    {
        <= 0m => 0,
        >= 100m => 100,
        _ => Math.Clamp((int)Math.Round(exact, MidpointRounding.AwayFromZero), 1, 99),
    };
}
