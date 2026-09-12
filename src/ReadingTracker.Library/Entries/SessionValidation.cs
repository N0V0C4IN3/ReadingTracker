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
}

public static class SessionValidation
{
    /// <summary>
    /// Checks that an amount of reading is one a person could have done. The only thing that is
    /// not is nothing at all.
    ///
    /// Reading more than the book has left in it is deliberately *not* refused. A reader who is
    /// twenty pages from the end and logs forty has either misremembered, or is holding an
    /// edition longer than the one we have a count for — and neither is worth throwing their
    /// reading away over. It is recorded as they stated it, their total stops at the whole book
    /// (see <see cref="Progress"/>), and they are asked whether that means they have finished.
    /// </summary>
    public static SessionProblem? Check(decimal amount) =>
        amount <= 0m ? SessionProblem.NotAnAmountOfReading : null;
}
