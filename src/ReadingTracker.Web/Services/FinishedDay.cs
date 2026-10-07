namespace ReadingTracker.Web.Services;

/// <summary>
/// Which day a book was finished, as the shelf says it to Library when a book becomes Finished:
/// the Reader's own today (the default, so nothing in the browser falls back to Library's UTC
/// date), a day they state, or <see cref="Unknown"/> when nobody knows it — an import can say a
/// book was read without saying when, and such a book counts toward no year's goal.
/// </summary>
public abstract record FinishedDay
{
    private FinishedDay()
    {
    }

    /// <summary>Today where the Reader is, midnight to midnight.</summary>
    public static FinishedDay Today { get; } = new ReadersToday();

    public static FinishedDay Unknown { get; } = new NotKnown();

    /// <summary>The day an import says, or unknown when it says a book was read but not when.</summary>
    public static FinishedDay Of(DateOnly? day) => day is { } known ? new On(known) : Unknown;

    /// <summary>A day the Reader, or a site they import from, states.</summary>
    public sealed record On(DateOnly Day) : FinishedDay;

    public sealed record ReadersToday : FinishedDay;

    public sealed record NotKnown : FinishedDay;
}
