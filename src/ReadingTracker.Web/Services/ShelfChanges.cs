namespace ReadingTracker.Web.Services;

/// <summary>
/// A word passed round the page when the shelf changes in a way something outside it cares
/// about — the header's goal badge, which counts finished books. The card that made the change
/// says so; whoever is listening asks Library again. One scoped instance per app.
/// </summary>
public sealed class ShelfChanges
{
    public event Action? Changed;

    public void Announce() => Changed?.Invoke();
}
