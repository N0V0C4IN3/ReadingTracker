using Microsoft.EntityFrameworkCore;
using ReadingTracker.Gateway.Devices;

namespace ReadingTracker.Gateway.Persistence;

/// <summary>
/// The Gateway's slice of the shared Postgres instance: DeviceTokens and nothing else. Per
/// ADR-0004 it is a schema of its own, and nothing in it is read by any other service — a
/// DeviceToken never leaves the Gateway, only the reader it stands for does (ADR-0014).
/// </summary>
public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public const string Schema = "gateway";

    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<DeviceToken>(token =>
        {
            token.HasKey(t => t.Id);

            token.Property(t => t.ReaderId).IsRequired();
            token.Property(t => t.Name).IsRequired();
            token.Property(t => t.SecretHash).IsRequired();

            // Every authenticated device request is one lookup by hash, and no two tokens can
            // share one.
            token.HasIndex(t => t.SecretHash).IsUnique();

            // A reader's Devices page lists their own.
            token.HasIndex(t => t.ReaderId);
        });
    }
}
