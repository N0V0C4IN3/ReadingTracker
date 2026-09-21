namespace ReadingTracker.Web.Components;

/// <summary>
/// The jackets on the welcome screen's deck, and which one it opens on.
/// </summary>
public static class Jackets
{
    /// <summary>
    /// The books in the deck: long out of copyright, with Open Library's cover for each.
    ///
    /// The ids are written down rather than looked up. A welcome screen that has to search an API
    /// before it knows what to draw is a welcome screen that waits — and Open Library rate limits
    /// a run of searches, so the wait is not hypothetical. Fetching seventeen known images is a
    /// different thing from asking seventeen questions first.
    ///
    /// The jackets themselves are served from wwwroot/covers rather than from Open Library, and
    /// the ids survive only as the filenames, which is where each one came from. Fetching them
    /// live was measured at roughly two seconds apiece: covers.openlibrary.org answers most ids
    /// with a 302 to archive.org, which then pulls the jpeg out of a zip. Seventeen of those, on
    /// the screen a signed-out reader sees first and lands back on after signing out, routinely
    /// spent the whole cap below waiting. They are 312 KB together and they never change.
    ///
    /// Every title here has been checked to have a cover. That is the curation the drawn jackets
    /// underneath cannot do for us: one drawn jacket in a deck of photographed ones does not read
    /// as a considered fallback, it reads as a broken image. Anna Karenina was dropped for exactly
    /// this reason — Open Library has no cover for it.
    ///
    /// wwwroot/index.html prefetches these same covers while the runtime is still starting, so
    /// that by the time this renders they are already in the cache. Keep the two lists in step —
    /// an id only in one place costs that jacket its head start rather than breaking anything.
    /// </summary>
    public static readonly (string Title, string Author, int CoverId)[] Wall =
    [
        ("Frankenstein", "Mary Shelley", 12356249),
        ("Middlemarch", "George Eliot", 252882),
        ("Moby-Dick", "Herman Melville", 10544254),
        ("Jane Eyre", "Charlotte Brontë", 8235363),
        ("The Odyssey", "Homer", 12474938),
        ("Dracula", "Bram Stoker", 12216503),
        ("Persuasion", "Jane Austen", 12824691),
        ("Walden", "H. D. Thoreau", 11248037),
        // Not 9009316, whose cover art reads "THE TIME MASCHINE" in display type.
        ("The Time Machine", "H. G. Wells", 6907719),
        ("Wuthering Heights", "Emily Brontë", 12818862),
        ("Crime and Punishment", "F. Dostoevsky", 13116014),
        ("The Awakening", "Kate Chopin", 8750241),
        ("Great Expectations", "Charles Dickens", 13322313),
        ("Dubliners", "James Joyce", 8216412),
        ("The Iliad", "Homer", 12621988),
        ("North and South", "E. Gaskell", 8242253),
        ("The Turn of the Screw", "Henry James", 181493),
    ];

    /// <summary>
    /// The book at the front when the deck is first seen: any of them, at random. The wall keeps
    /// its order around whichever is chosen, so the procession is the same one from a different
    /// starting point, not a shuffle.
    /// </summary>
    public static int PickFront(Random random) => random.Next(Wall.Length);
}
