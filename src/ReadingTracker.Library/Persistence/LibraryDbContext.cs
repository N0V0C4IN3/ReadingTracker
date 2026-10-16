using Microsoft.EntityFrameworkCore;
using ReadingTracker.Library.Entries;

namespace ReadingTracker.Library.Persistence;

/// <summary>
/// Library's slice of the shared Postgres instance. Per ADR-0004 every service owns its own
/// schema and never reads another service's tables — Catalog's Books are reached over HTTP,
/// never by querying its schema.
/// </summary>
public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public const string Schema = "library";

    public DbSet<LibraryEntry> LibraryEntries => Set<LibraryEntry>();

    public DbSet<ReadingSession> ReadingSessions => Set<ReadingSession>();

    public DbSet<ReadingGoal> ReadingGoals => Set<ReadingGoal>();

    public DbSet<Quote> Quotes => Set<Quote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<LibraryEntry>(entry =>
        {
            entry.HasKey(e => e.Id);

            entry.Property(e => e.ReaderId).IsRequired();

            // Stored by name so adding a status or tracking method later needs no migration.
            entry.Property(e => e.Status).HasConversion<string>().IsRequired();
            entry.Property(e => e.TrackingMethod).HasConversion<string>().IsRequired();

            // A book appears once in a reader's library — but two readers may each have it.
            entry.HasIndex(e => new { e.ReaderId, e.BookId }).IsUnique();

            // The yearly goal counts a reader's books by the day they were finished.
            entry.HasIndex(e => new { e.ReaderId, e.FinishedOn });
        });

        modelBuilder.Entity<ReadingGoal>(goal =>
        {
            // One goal per reader per year, and that pair is the whole identity.
            goal.HasKey(g => new { g.ReaderId, g.Year });
        });

        modelBuilder.Entity<ReadingSession>(session =>
        {
            session.HasKey(s => s.Id);

            // Sessions belong to an entry and go when it goes.
            session.HasOne<LibraryEntry>()
                .WithMany()
                .HasForeignKey(s => s.LibraryEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            session.Property(s => s.Unit).HasConversion<string>().IsRequired();
            session.Property(s => s.Source).HasConversion<string>().IsRequired();

            // Reading a book's history, and finding its latest session, are the two things
            // ever asked of this table.
            session.HasIndex(s => new { s.LibraryEntryId, s.OccurredAt });
        });

        modelBuilder.Entity<Quote>(quote =>
        {
            quote.HasKey(q => q.Id);

            // Quotes belong to an entry and go when it goes.
            quote.HasOne<LibraryEntry>()
                .WithMany()
                .HasForeignKey(q => q.LibraryEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            quote.Property(q => q.HighlightId).IsRequired();
            quote.Property(q => q.Passage).IsRequired();

            // A highlight is one Quote on its entry, however many times it is sent (ADR-0017). It
            // leads with the entry, so it also serves listing an entry's Quotes and counting them.
            quote.HasIndex(q => new { q.LibraryEntryId, q.HighlightId }).IsUnique();
        });
    }
}
