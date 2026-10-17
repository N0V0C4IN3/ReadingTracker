namespace ReadingTracker.Library.Entries;

/// <summary>
/// What a status change says about the day a book was finished, read from the request once so
/// nothing past the endpoint meets a combination that cannot be. The Web says the same three
/// things in its own FinishedDay; a caller that says nothing (the KOReader plugin) gets
/// <see cref="NotSaid"/>.
/// </summary>
public abstract record FinishedDay
{
    private FinishedDay()
    {
    }

    /// <summary>
    /// The day as a status change gives it: a day, that it is unknown, or nothing said. Null when
    /// what it gives cannot be: a day or an unknown one with a status other than Finished, or both
    /// at once — with the field to blame and why.
    /// </summary>
    public static FinishedDay? From(ReadingStatus status, DateOnly? finishedOn, bool dayUnknown, out (string Field, string Reason) refused)
    {
        refused = default;

        if (finishedOn is null && !dayUnknown)
        {
            return new NotSaid();
        }

        var field = dayUnknown ? "dayUnknown" : "finishedOn";

        if (status != ReadingStatus.Finished)
        {
            refused = (field, "A day finished, or that it is not known, only goes with the Finished status.");
            return null;
        }

        if (finishedOn is { } day && dayUnknown)
        {
            refused = (field, "Say the day finished, or that it is not known, not both.");
            return null;
        }

        return finishedOn is { } known ? new On(known) : new Unknown();
    }

    /// <summary>Nothing said: a caller that knows no reader's zone. Arriving at Finished stamps today in UTC.</summary>
    public sealed record NotSaid : FinishedDay;

    /// <summary>The day the Reader, or a site they import from, says.</summary>
    public sealed record On(DateOnly Day) : FinishedDay;

    /// <summary>Finished, on a day nobody knows: no day, and no year's goal counts it.</summary>
    public sealed record Unknown : FinishedDay;
}
