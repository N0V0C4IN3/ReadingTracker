namespace ReadingTracker.Web.Components.WelcomePrototype;

/// <summary>
/// PROTOTYPE — throwaway. Shared data for the welcome-screen variants, so each one can disagree
/// about layout without also disagreeing about which books it is showing. Sharing the books is
/// the point; nothing here may grow into a shared layout.
/// </summary>
public sealed record PrototypeBook(string Title, string Author, int? CoverId, int Tone)
{
    /// <summary>
    /// Open Library's cover, the size the carousel wants. Null where Open Library has no cover
    /// for the book, which is itself part of what the "real covers" question is asking.
    /// </summary>
    public string? CoverUrl => CoverId is null ? null : $"https://covers.openlibrary.org/b/id/{CoverId}-M.jpg";
}

public static class PrototypeBooks
{
    /// <summary>How many jacket designs the prototype stylesheet draws.</summary>
    public const int Tones = 6;

    /// <summary>
    /// Long out of copyright. <c>CoverId</c> is Open Library's, looked up once and written down
    /// rather than fetched at render time — a landing page that waits on a search API to know
    /// what to draw is the thing being evaluated, not something to build the evaluation on.
    /// </summary>
    public static readonly IReadOnlyList<PrototypeBook> All =
    [
        new("Frankenstein", "Mary Shelley", 12356249, 0),
        new("Middlemarch", "George Eliot", 252882, 1),
        new("Moby-Dick", "Herman Melville", 10544254, 2),
        new("Jane Eyre", "Charlotte Brontë", 8235363, 3),
        new("The Odyssey", "Homer", 12474938, 4),
        new("Dracula", "Bram Stoker", 12216503, 5),
        new("Persuasion", "Jane Austen", 12824691, 0),
        new("Walden", "H. D. Thoreau", 11248037, 1),
        new("The Time Machine", "H. G. Wells", 6907719, 2),
        new("Wuthering Heights", "Emily Brontë", 12818862, 3),
        new("Crime and Punishment", "F. Dostoevsky", 13116014, 4),
        new("The Awakening", "Kate Chopin", 8750241, 5),
        new("Great Expectations", "Charles Dickens", 13322313, 0),
        new("Anna Karenina", "Leo Tolstoy", null, 1),
        new("Dubliners", "James Joyce", 8216412, 2),
        new("The Iliad", "Homer", 12621988, 3),
        new("North and South", "E. Gaskell", 8242253, 4),
        new("The Turn of the Screw", "Henry James", 181493, 5),
    ];
}
