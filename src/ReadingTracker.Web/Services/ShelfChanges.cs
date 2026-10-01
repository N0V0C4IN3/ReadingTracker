namespace ReadingTracker.Web.Services;

/// <summary>
/// A word passed round the page when the shelf changes in a way something outside it cares
/// about — the header's goal badge, which counts finished books. The word carries nothing: the
/// count depends on Finished on, which Library stamps, so whoever is listening asks Library
/// again. GatewayLibraryClient says it, after a status that took or an entry removed, so
/// whatever made the change — a card, the Book page, an import — need not remember to.
/// One scoped instance per app.
/// </summary>
public sealed class ShelfChanges
{
    public event Action? Changed;

    public void Announce() => Changed?.Invoke();
}
