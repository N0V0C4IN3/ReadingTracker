using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace ReadingTracker.Library.Persistence;

public static class LibrarySchema
{
    /// <summary>
    /// Library's own advisory lock key, distinct from Catalog's (ADR-0006). Sharing a key
    /// across services would make unrelated deployments queue behind each other.
    /// </summary>
    private const long MigrationLockKey = 8_233_071_002;

    /// <summary>
    /// Brings Library's schema up to date, serialised across replicas. ADR-0005 puts services
    /// on a platform that can cold-start several replicas at once, and EF Core's migrations
    /// are not safe to run concurrently.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database;

        // Advisory locks live for the length of a session, so the lock and the migration have
        // to share a connection. Opening it here means EF reuses it.
        var connection = database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

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
