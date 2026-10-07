namespace ReadingTracker.Web.Services;

/// <summary>
/// Which day a book was finished, as the shelf says it to Library when a book becomes Finished:
/// the Reader's own today, a day they state, or none when nobody knows it — an import can say a
/// book was read without saying when, and such a book counts toward no year's goal.
/// <see cref="GatewayLibraryClient"/> takes no FinishedDay as <see cref="Today"/>, so nothing in
/// the browser falls back to Library's UTC date.
/// </summary>
public abstract record FinishedDay
{
    private FinishedDay()
    {
    }

    /// <summary>The day an import says, or unknown when it says a book was read but not when.</summary>
    public static FinishedDay Of(DateOnly? day) => day is { } known ? new On(known) : new Unknown();

    /// <summary>Today where the Reader is, midnight to midnight, by the client's clock.</summary>
    public sealed record Today : FinishedDay;

    /// <summary>A day the Reader, or a site they import from, states.</summary>
    public sealed record On(DateOnly Day) : FinishedDay;

    /// <summary>Finished, on a day nobody knows.</summary>
    public sealed record Unknown : FinishedDay;
}
