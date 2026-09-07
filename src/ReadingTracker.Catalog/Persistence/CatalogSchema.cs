using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace ReadingTracker.Catalog.Persistence;

public static class CatalogSchema
{
    /// <summary>
    /// Arbitrary but fixed. Every Catalog replica contends for this same Postgres advisory
    /// lock, so only one applies migrations at a time. Other services must pick their own key.
    /// </summary>
    private const long MigrationLockKey = 8_233_071_001;

    /// <summary>
    /// Brings Catalog's schema up to date, serialised across replicas.
    /// ADR-0005 puts services on a platform that scales to zero and can cold-start several
    /// replicas at once, so without this lock concurrent boots would race on the same schema.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database;

        // Advisory locks are held for the life of a session, so the lock and the migration
        // must run on the same connection. Opening it here means EF reuses it rather than
        // opening a second one.
        var connection = database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        // Postgres-specific: this is the same instance ADR-0004 gives every service a schema on.
        await ExecuteAsync(connection, $"SELECT pg_advisory_lock({MigrationLockKey})", cancellationToken);

        try
        {
            await database.MigrateAsync(cancellationToken);
        }
        finally
        {
            await ExecuteAsync(connection, $"SELECT pg_advisory_unlock({MigrationLockKey})", cancellationToken);
        }
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
