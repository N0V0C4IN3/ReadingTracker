namespace ReadingTracker.Web.Services;

/// <summary>
/// Which day a book was finished, as the shelf says it to Library when it finishes one
/// (<see cref="GatewayLibraryClient.FinishAsync"/>): the Reader's own today, a day they state, or
/// none when nobody knows it — an import can say a book was read without saying when, and such a
/// book counts toward no year's goal. Always said, so nothing in the browser falls back to
/// Library's UTC date.
/// </summary>
public abstract record FinishedDay
{
    private FinishedDay()
    {
    }

    /// <summary>
    /// The day another site's export gives a book it says was read: that day, or unknown when it
    /// left it blank. Blank means unknown only here; the Reader finishing a book now is
    /// <see cref="Today"/>.
    /// </summary>
    public static FinishedDay FromExport(DateOnly? day) => day is { } known ? new On(known) : new Unknown();

    /// <summary>Today where the Reader is, midnight to midnight, by the client's clock.</summary>
    public sealed record Today : FinishedDay;

    /// <summary>A day the Reader, or a site they import from, states.</summary>
    public sealed record On(DateOnly Day) : FinishedDay;

    /// <summary>Finished, on a day nobody knows.</summary>
    public sealed record Unknown : FinishedDay;
}
