using Microsoft.EntityFrameworkCore;

namespace ReadingTracker.Catalog.Persistence;

/// <summary>
/// Catalog's slice of the shared Postgres instance. Per ADR-0004 every service owns its own
/// schema and never reads another service's tables.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(Schema);
}
