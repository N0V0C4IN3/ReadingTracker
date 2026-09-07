namespace ReadingTracker.Catalog.Tests;

/// <summary>
/// One application and one Postgres container for the whole suite. Per-class fixtures would
/// start a container each, in parallel, which is both slow and flaky under contention.
/// Sharing also means tests run sequentially, so setting a provider stub in one test cannot
/// be trampled by another running at the same time.
///
/// The database is shared, so tests must use distinct ISBNs rather than relying on a clean slate.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CatalogApiCollection : ICollectionFixture<CatalogApiFixture>
{
    public const string Name = "catalog-api";
}
