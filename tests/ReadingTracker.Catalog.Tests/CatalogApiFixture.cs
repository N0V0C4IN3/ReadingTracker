using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ReadingTracker.Catalog.Books;
using Testcontainers.PostgreSql;

namespace ReadingTracker.Catalog.Tests;

/// <summary>
/// Boots the real Catalog application in-process against a throwaway Postgres container.
/// This is the single seam the Catalog test suite exercises: tests drive the service through
/// its HTTP surface, never through its internals.
///
/// External book providers are stubbed at the network boundary rather than at
/// <see cref="IBookProvider"/>, so provider code (including how it reads a provider's JSON)
/// runs for real while no test ever touches the live API.
/// </summary>
public sealed class CatalogApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public StubHttpMessageHandler GoogleBooks { get; } = new();

    public StubHttpMessageHandler OpenLibrary { get; } = new();

    public CatalogApiFixture() =>
        // Unless a test says otherwise, Open Library simply has no match for the ISBN.
        OpenLibrary.Respond = _ => StubHttpMessageHandler.Json("{}");

    public async Task InitializeAsync() => await _postgres.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:CatalogDb", _postgres.GetConnectionString());

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<GoogleBooksProvider>().ConfigurePrimaryHttpMessageHandler(() => GoogleBooks);
            services.AddHttpClient<OpenLibraryProvider>().ConfigurePrimaryHttpMessageHandler(() => OpenLibrary);
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Shut the host down first so its pooled connections close before the database goes away.
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
