using Testcontainers.PostgreSql;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// One throwaway Postgres for every Gateway fixture in the run. The Gateway now owns a schema
/// (DeviceTokens), so every fixture that boots it needs a database to migrate — three fixtures
/// booting three containers would triple the slowest part of the suite for no isolation worth
/// having: the fixtures use distinct readers, and the schema is the same one every time.
///
/// Started on first use and never stopped by hand; Testcontainers' reaper removes it when the
/// test process exits.
/// </summary>
public static class TestPostgres
{
    private static readonly Lazy<Task<string>> Started = new(StartAsync);

    public static Task<string> ConnectionStringAsync() => Started.Value;

    private static async Task<string> StartAsync()
    {
        var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await container.StartAsync();
        return container.GetConnectionString();
    }
}
