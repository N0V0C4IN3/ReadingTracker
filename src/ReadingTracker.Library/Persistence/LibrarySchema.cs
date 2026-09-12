using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ReadingTracker.Library.Persistence;

public static class LibrarySchema
{
    /// <summary>
    /// Library's own advisory lock key, distinct from Catalog's (ADR-0006). Sharing a key
    /// across services would make unrelated deployments queue behind each other.
    /// </summary>
    private const long MigrationLockKey = 8_233_071_002;

    /// <summary>
    /// How long to keep trying to reach Postgres before giving up on it.
    ///
    /// This is not a substitute for compose's depends_on, which already waits for postgres to
    /// report healthy. The gap is that depends_on only governs `docker compose up`. When the
    /// machine itself reboots, Docker's restart policy brings every container back at once and
    /// ignores compose's ordering entirely — so on a Pi coming back from a power cut this raced
    /// Postgres's crash recovery and lost, with `57P03: the database system is starting up`
    /// thrown straight out of Main.
    ///
    /// Losing that race was unrecoverable rather than merely slow. The process died, but the
    /// container stayed up around it, so `restart: unless-stopped` saw nothing wrong and never
    /// restarted anything. The service sat there answering connection refused until somebody
    /// noticed and restarted it by hand — forty minutes, the first time this happened.
    ///
    /// Three minutes because the wait is bounded by how long Postgres takes to replay its WAL,
    /// not by anything here; a database that is still not up by then is broken rather than busy,
    /// and failing is the right answer.
    /// </summary>
    private static readonly TimeSpan LongestWaitForPostgres = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Brings Library's schema up to date, serialised across replicas. ADR-0005 puts services
    /// on a platform that can cold-start several replicas at once, and EF Core's migrations
    /// are not safe to run concurrently.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database;
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(LibrarySchema));

        // Advisory locks live for the length of a session, so the lock and the migration have
        // to share a connection. Opening it here means EF reuses it.
        var connection = database.GetDbConnection();
        await OpenWhenReadyAsync(connection, logger, cancellationToken);

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

    /// <summary>
    /// Opens the connection, waiting out a database that is on its way up.
    ///
    /// Only the connection is retried, never the migration itself: a migration that fails has
    /// found something wrong with the schema, and running it again will not fix that.
    /// </summary>
    private static async Task OpenWhenReadyAsync(
        DbConnection connection, ILogger logger, CancellationToken cancellationToken)
    {
        var giveUpAt = DateTimeOffset.UtcNow + LongestWaitForPostgres;
        var wait = TimeSpan.FromSeconds(1);
        var announced = false;

        while (true)
        {
            try
            {
                await connection.OpenAsync(cancellationToken);
                return;
            }
            catch (Exception exception)
                when (IsStillComingUp(exception) && DateTimeOffset.UtcNow + wait < giveUpAt)
            {
                // Once, not once per attempt: this is a normal few seconds of a normal boot, and
                // a line a second for a minute would bury the one thing worth reading.
                if (!announced)
                {
                    logger.LogInformation(
                        "Postgres is not accepting queries yet ({Reason}). Waiting up to {Seconds}s.",
                        exception.Message, LongestWaitForPostgres.TotalSeconds);
                    announced = true;
                }

                await Task.Delay(wait, cancellationToken);
                wait = TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds * 2, 5));
            }
        }
    }

    /// <summary>
    /// Whether this is a database that is coming up rather than one that is wrong.
    ///
    /// A Postgres that is present but not yet serving answers with a class 57 state; a Postgres
    /// that has not opened its socket yet does not answer at all, which surfaces as a plain
    /// NpgsqlException. Anything else — a bad password, a missing database — is not going to be
    /// any different in five seconds, so it is left to throw.
    /// </summary>
    private static bool IsStillComingUp(Exception exception) => exception switch
    {
        PostgresException postgres => postgres.SqlState
            is PostgresErrorCodes.CannotConnectNow
            or PostgresErrorCodes.AdminShutdown
            or PostgresErrorCodes.CrashShutdown,
        NpgsqlException => true,
        _ => false,
    };

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
