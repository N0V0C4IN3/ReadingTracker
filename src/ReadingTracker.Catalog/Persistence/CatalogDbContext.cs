using Microsoft.EntityFrameworkCore;
using ReadingTracker.Catalog.Books;

namespace ReadingTracker.Catalog.Persistence;

/// <summary>
/// Catalog's slice of the shared Postgres instance. Per ADR-0004 every service owns its own
/// schema and never reads another service's tables.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<Book> Books => Set<Book>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Book>(book =>
        {
            book.HasKey(entity => entity.Id);

            book.Property(entity => entity.Title).IsRequired();

            // Stored by name so adding a source later needs no migration.
            book.Property(entity => entity.Source).HasConversion<string>().IsRequired();

            // One Book per ISBN: this is what makes duplicate provider matches collapse,
            // including when two requests race to cache the same title.
            book.HasIndex(entity => entity.Isbn)
                .IsUnique()
                .HasFilter(@"""Isbn"" IS NOT NULL");

            // How editions are told apart when a provider reports no ISBN.
            book.HasIndex(entity => new { entity.Source, entity.ExternalId });
        });
    }
}
