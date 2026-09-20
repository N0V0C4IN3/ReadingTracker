namespace ReadingTracker.Web.Services;

/// <summary>
/// How the shelf is ordered: grouped by status, the groups in the order a reader thinks of
/// them — what is being read, then what is queued, then what is done, then what is paused,
/// then what was given up on. Library hands entries back newest first, and within a group
/// that order survives — except among the finished, where the book finished most recently
/// comes first, and one finished before the shelf kept dates comes after those that have one.
/// </summary>
public static class ShelfOrder
{
    /// <summary>The statuses, top to bottom. Anything Library invents later goes to the end.</summary>
    public static readonly IReadOnlyList<string> Statuses = ["Reading", "WantToRead", "Finished", "OnHold", "Dropped"];

    public static IReadOnlyList<LibraryEntry> ByStatus(IReadOnlyList<LibraryEntry> entries) =>
        // OrderBy is stable, so where the keys tie the newest-first order survives. A missing
        // date sorts below every real one when descending, which puts the undated last.
        [.. entries.OrderBy(entry => Rank(entry.Status)).ThenByDescending(FinishedOn)];

    private static int Rank(string status)
    {
        var at = Statuses.ToList().IndexOf(status);
        return at < 0 ? Statuses.Count : at;
    }

    private static DateOnly? FinishedOn(LibraryEntry entry) => entry.Status == "Finished" ? entry.FinishedOn : null;
}
