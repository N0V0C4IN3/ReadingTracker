using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace ReadingTracker.Catalog.Tests;

/// <summary>
/// Boots the real Catalog application in-process against a throwaway Postgres container.
/// This is the single seam the Catalog test suite exercises: tests drive the service through
/// its HTTP surface, never through its internals.
/// </summary>
public sealed class CatalogApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:CatalogDb", _postgres.GetConnectionString());

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Shut the host down first so its pooled connections close before the database goes away.
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
