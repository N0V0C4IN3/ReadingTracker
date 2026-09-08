namespace ReadingTracker.Library.Entries;

/// <summary>Why a ReadingSession could not be recorded as given.</summary>
public enum SessionProblem
{
    /// <summary>The reader has no such entry — including because it is someone else's.</summary>
    NoSuchEntry,

    /// <summary>The entry has no such session — including because it was already deleted.</summary>
    NoSuchSession,

    EndsBeforeItStarts,

    RunsPastTheEndOfTheBook,

    PositionOutOfRange,
}

public static class SessionValidation
{
    /// <summary>
    /// Checks a session's positions make sense. <paramref name="totalPages"/> is null when the
    /// book's length is unknown, in which case there is nothing to be past the end of.
    /// </summary>
    public static SessionProblem? Check(
        decimal startPosition,
        decimal endPosition,
        TrackingMethod unit,
        int? totalPages)
    {
        if (endPosition < startPosition)
        {
            return SessionProblem.EndsBeforeItStarts;
        }

        if (startPosition < 0)
        {
            return SessionProblem.PositionOutOfRange;
        }

        if (unit is TrackingMethod.Percentage)
        {
            return endPosition > 100 ? SessionProblem.PositionOutOfRange : null;
        }

        return totalPages is { } total && endPosition > total
            ? SessionProblem.RunsPastTheEndOfTheBook
            : null;
    }
}
