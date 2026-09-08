using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ReadingTracker.Library.Catalog;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace ReadingTracker.Library.Tests;

/// <summary>
/// Boots the real Library application in-process against throwaway Postgres and RabbitMQ
/// containers. This is the single seam the Library test suite exercises: tests drive the
/// service through its HTTP surface, never through its internals.
///
/// Catalog is stubbed at the network boundary rather than by replacing <see cref="CatalogClient"/>,
/// so Library's own client code — including how it reads Catalog's JSON — runs for real
/// while no test needs a running Catalog.
/// </summary>
public sealed class LibraryApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3.13-management").Build();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public StubHttpMessageHandler Catalog { get; } = new();

    public async Task InitializeAsync() =>
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

    /// <summary>
    /// A client that speaks for one reader, the way the Gateway does. Tests give each reader a
    /// distinct id so their shelves cannot see each other.
    /// </summary>
    public HttpClient ClientFor(string readerId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Reader-Id", readerId);
        return client;
    }

    /// <summary>
    /// Puts a book on a reader's shelf and returns the new entry's id. Pass a null
    /// <paramref name="totalPages"/> for a book whose length nobody knows.
    /// </summary>
    public async Task<Guid> AddBookAsync(HttpClient client, int? totalPages = 200)
    {
        var bookId = Guid.NewGuid();
        CatalogHasBook(bookId, totalPages);

        var response = await client.PostAsJsonAsync("/api/library", new { bookId });
        return (await response.Content.ReadFromJsonAsync<AddedEntry>())!.Id;
    }

    /// <summary>
    /// Stubs Catalog to answer both the single-book lookup used when adding and the batch
    /// lookup used when listing.
    /// </summary>
    public void CatalogHasBook(Guid bookId, int? totalPages = 200) =>
        Catalog.Respond = request =>
        {
            var book = $$"""
                {
                  "id": "{{bookId}}",
                  "title": "A Book",
                  "authors": ["A. Writer"],
                  "totalPages": {{totalPages?.ToString() ?? "null"}}
                }
                """;

            return StubHttpMessageHandler.Json(
                request.RequestUri!.Query.Contains("ids=") ? $"[{book}]" : book);
        };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:LibraryDb", _postgres.GetConnectionString());
        builder.UseSetting("RabbitMq:ConnectionString", _rabbitMq.GetConnectionString());

        builder.ConfigureTestServices(services =>
            services.AddHttpClient<CatalogClient>().ConfigurePrimaryHttpMessageHandler(() => Catalog));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Shut the host down first so its pooled connections close before the database goes away.
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }

    private sealed record AddedEntry(Guid Id);
}
