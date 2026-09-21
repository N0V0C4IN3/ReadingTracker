namespace ReadingTracker.Web.Services;

/// <summary>
/// The shelf as the reader left it — which tab they were on, and what was on it — so that coming
/// back to it, from a book's page or by the browser's back button, finds the shelf where it was
/// rather than where it opens by default, and so that a book's page can start on what the shelf
/// already knew of the book before Library has answered for it. One scoped instance per app: it
/// lives as long as the page does, and no longer, since a shelf reloaded is a shelf opened afresh.
/// </summary>
public sealed class ShelfMemory
{
    /// <summary>
    /// The entries as Library last listed them for the shelf. A head start, not an answer: a
    /// page that begins from one of these still asks Library, and shows what it says.
    /// </summary>
    public IReadOnlyList<LibraryEntry> Entries { get; private set; } = [];

    public void Keep(IReadOnlyList<LibraryEntry>? entries) => Entries = entries ?? [];

    /// <summary>What the shelf knew of one entry the last time it was listed, if it was.</summary>
    public LibraryEntry? Recall(Guid entryId) => Entries.FirstOrDefault(entry => entry.Id == entryId);

    /// <summary>Whether the reader has left the shelf on a tab at all this visit.</summary>
    public bool Remembers { get; private set; }

    /// <summary>The status the shelf was narrowed to; null for everything. Meaningful only while <see cref="Remembers"/>.</summary>
    public string? Tab { get; private set; }

    public void Remember(string? tab)
    {
        Tab = tab;
        Remembers = true;
    }

    /// <summary>Another reader's shelf is another shelf.</summary>
    public void Forget()
    {
        Tab = null;
        Remembers = false;
        Entries = [];
    }
}
