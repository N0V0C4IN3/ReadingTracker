namespace ReadingTracker.Library.Entries;

/// <summary>Why a ReadingSession could not be recorded as given.</summary>
public enum SessionProblem
{
    /// <summary>The reader has no such entry — including because it is someone else's.</summary>
    NoSuchEntry,

    /// <summary>The entry has no such session — including because it was already deleted.</summary>
    NoSuchSession,

    /// <summary>Nothing, or less than nothing, was read. A session records some reading.</summary>
    NotAnAmountOfReading,

    /// <summary>More was read in one sitting than the book has in it, which is a typo.</summary>
    LongerThanTheBook,
}

public static class SessionValidation
{
    /// <summary>
    /// Checks that an amount of reading is one a person could have done in a sitting.
    /// <paramref name="totalPages"/> is null when the book's length is unknown, in which case
    /// there is no length to have read more than.
    ///
    /// Only the session in front of us is measured against the book, never the running total.
    /// Someone who reads a book twice has read twice its length, and refusing to record that
    /// would make re-reading unloggable — which is a far more common thing to do than typing a
    /// number bigger than the whole book.
    /// </summary>
    public static SessionProblem? Check(decimal amount, TrackingMethod unit, int? totalPages)
    {
        if (amount <= 0m)
        {
            return SessionProblem.NotAnAmountOfReading;
        }

        if (unit is TrackingMethod.Percentage)
        {
            return amount > 100m ? SessionProblem.LongerThanTheBook : null;
        }

        return totalPages is { } total && amount > total
            ? SessionProblem.LongerThanTheBook
            : null;
    }
}
