namespace ReadingTracker.Web.Services;

/// <summary>
/// How the whole shelf is ordered when no status tab narrows it. Library hands entries back
/// newest first; the books being read are the ones a reader opens the shelf for, so they come
/// to the top, and everything else keeps the order it came in.
/// </summary>
public static class ShelfOrder
{
    public static IReadOnlyList<LibraryEntry> ReadingFirst(IReadOnlyList<LibraryEntry> entries) =>
        // OrderBy is stable, so within each half the newest-first order survives.
        [.. entries.OrderBy(entry => entry.Status == "Reading" ? 0 : 1)];
}
