namespace ReadingTracker.Library.Tests;

/// <summary>
/// One application and one set of containers for the whole suite. Catalog's suite started
/// with per-class fixtures and had to move away from them: a container per class, started in
/// parallel, was slow and failed under contention. Library starts where Catalog ended up.
///
/// The database is shared, so tests must not assume a clean slate.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LibraryApiCollection : ICollectionFixture<LibraryApiFixture>
{
    public const string Name = "library-api";
}
