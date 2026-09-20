namespace ReadingTracker.Web.Services;

/// <summary>
/// An import running in the background of the app: started from the Import page and carrying
/// on whether or not the reader stays on it, with its progress in the header. One per app —
/// a second file waits for the first to finish — and it lives as long as the tab does; closing
/// the tab ends it, and what had landed stays landed.
///
/// The Gateway paces lookups per reader (ADR-0013), so a shelf of fifty waits out a minute at
/// every tenth book. The wait is said as a countdown, from the Gateway's own Retry-After: a
/// number that comes down reads as work; a line that says "waiting" reads as stuck.
/// </summary>
public sealed class ImportJob(GatewayCatalogClient catalog, GatewayLibraryClient library, ShelfChanges shelf)
{
    public IReadOnlyList<ImportedBook> Books { get; private set; } = [];

    public IReadOnlyList<ImportResult> Results => _results;

    /// <summary>The book being looked up now, or null between books and when finished.</summary>
    public string? Current { get; private set; }

    /// <summary>Seconds left of a pause for the Gateway's pace, or null when not pausing.</summary>
    public int? WaitingSeconds { get; private set; }

    public bool Running { get; private set; }

    public bool Done { get; private set; }

    public event Action? Changed;

    private readonly List<ImportResult> _results = [];

    public int Count(ImportOutcome outcome) => _results.Count(result => result.Outcome == outcome);

    /// <summary>Begins an import, unless one is running. Returns whether it began.</summary>
    public bool Start(IReadOnlyList<ImportedBook> books)
    {
        if (Running)
        {
            return false;
        }

        Books = books;
        _results.Clear();
        Current = null;
        WaitingSeconds = null;
        Done = false;
        Running = true;

        _ = RunAsync();
        return true;
    }

    /// <summary>Forgets a finished import so another file can be chosen.</summary>
    public void Clear()
    {
        if (Running)
        {
            return;
        }

        Books = [];
        _results.Clear();
        Done = false;
        Announce();
    }

    private async Task RunAsync()
    {
        var importer = new LibraryImporter(catalog, library, WaitAsync);

        try
        {
            foreach (var book in Books)
            {
                Current = book.Title;
                Announce();

                _results.Add(await importer.ImportAsync(book, CancellationToken.None));
                Announce();
            }
        }
        catch (Exception)
        {
            // Whatever went wrong mid-way, what landed is in Results; the page says how many.
        }
        finally
        {
            Current = null;
            WaitingSeconds = null;
            Running = false;
            Done = true;
            Announce();
            shelf.Announce();
        }
    }

    /// <summary>A pause said out loud, a second at a time.</summary>
    private async Task WaitAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        var seconds = (int)Math.Ceiling(wait.TotalSeconds);

        for (var left = seconds; left > 0; left--)
        {
            WaitingSeconds = left;
            Announce();
            await Task.Delay(1000, cancellationToken);
        }

        WaitingSeconds = null;
        Announce();
    }

    private void Announce() => Changed?.Invoke();
}
