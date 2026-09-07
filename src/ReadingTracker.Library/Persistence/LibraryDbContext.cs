using Microsoft.EntityFrameworkCore;

namespace ReadingTracker.Library.Persistence;

/// <summary>
/// Library's slice of the shared Postgres instance. Per ADR-0004 every service owns its own
/// schema and never reads another service's tables — Catalog's Books are reached over HTTP,
/// never by querying its schema.
/// </summary>
public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public const string Schema = "library";

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(Schema);
}
