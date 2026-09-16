using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReadingTracker.Catalog.Books;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

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

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3.13-management").Build();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public StubHttpMessageHandler GoogleBooks { get; } = new();

    public StubHttpMessageHandler OpenLibrary { get; } = new();

    /// <summary>
    /// The Google Books key this Catalog runs with. A secret in every way but one: a test has
    /// to know it to prove it never shows up anywhere it should not.
    /// </summary>
    public const string GoogleBooksApiKey = "AIzaTestKeyThatMustNeverBeLogged";

    /// <summary>Everything Catalog has logged, so a test can assert on what it never said.</summary>
    public IReadOnlyList<string> Logs => _logs.Lines;

    private readonly LogCollector _logs = new();

    public CatalogApiFixture() =>
        // Unless a test says otherwise, Open Library simply has no match for the ISBN.
        OpenLibrary.Respond = _ => StubHttpMessageHandler.Json("{}");

    public async Task InitializeAsync() =>
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:CatalogDb", _postgres.GetConnectionString());
        builder.UseSetting("RabbitMq:ConnectionString", _rabbitMq.GetConnectionString());
        builder.UseSetting("GoogleBooks:ApiKey", GoogleBooksApiKey);

        builder.ConfigureLogging(logging => logging.AddProvider(_logs));

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<GoogleBooksProvider>().ConfigurePrimaryHttpMessageHandler(() => GoogleBooks);
            services.AddHttpClient<OpenLibraryProvider>().ConfigurePrimaryHttpMessageHandler(() => OpenLibrary);
        });
    }

    /// <summary>
    /// Keeps every line Catalog logs, formatted the way a real sink would see it. Registered
    /// alongside the ordinary providers rather than instead of them, so the application's
    /// logging configuration — its levels in particular — is what is under test.
    /// </summary>
    private sealed class LogCollector : ILoggerProvider
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCollector collector, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (collector._lines)
                {
                    collector._lines.Add($"{category}: {formatter(state, exception)}");
                }
            }
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Shut the host down first so its pooled connections close before the database goes away.
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }
}
