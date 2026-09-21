namespace ReadingTracker.Web.Services;

/// <summary>
/// The shelf as the reader left it — which tab they were on — so that coming back to it, from
/// a book's page or by the browser's back button, finds the shelf where it was rather than
/// where it opens by default. One scoped instance per app: it lives as long as the page does,
/// and no longer, since a shelf reloaded is a shelf opened afresh.
/// </summary>
public sealed class ShelfMemory
{
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
    }
}
