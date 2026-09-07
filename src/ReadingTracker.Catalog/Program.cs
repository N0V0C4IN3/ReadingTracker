using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ReadingTracker.Catalog.Books;
using ReadingTracker.Catalog.Persistence;

// Load .env into the process before the builder reads environment variables, so local
// development can keep secrets in a gitignored file. Deployed environments have no .env
// and supply configuration directly; TraversePath finds the file from the repo root when
// running via `dotnet run --project`.
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.Configure<GoogleBooksOptions>(
    builder.Configuration.GetSection(GoogleBooksOptions.SectionName));

// GOOGLE_BOOKS_API_KEY is accepted as an alias for GoogleBooks:ApiKey, since a flat
// name is what .env files and container platforms conventionally use.
builder.Services.PostConfigure<GoogleBooksOptions>(googleBooks =>
    googleBooks.ApiKey ??= builder.Configuration["GOOGLE_BOOKS_API_KEY"]);

builder.Services.Configure<OpenLibraryOptions>(
    builder.Configuration.GetSection(OpenLibraryOptions.SectionName));

builder.Services.AddHttpClient<GoogleBooksProvider>((serviceProvider, client) =>
    client.BaseAddress = serviceProvider.GetRequiredService<IOptions<GoogleBooksOptions>>().Value.BaseAddress);

builder.Services.AddHttpClient<OpenLibraryProvider>((serviceProvider, client) =>
    client.BaseAddress = serviceProvider.GetRequiredService<IOptions<OpenLibraryOptions>>().Value.BaseAddress);

// Order matters: Google Books has the more consistent metadata, so it answers first and
// Open Library covers its gaps and outages.
builder.Services.AddScoped<IBookProvider>(services => services.GetRequiredService<GoogleBooksProvider>());
builder.Services.AddScoped<IBookProvider>(services => services.GetRequiredService<OpenLibraryProvider>());
builder.Services.AddScoped<BookProviderChain>();

// Fail at startup with a clear message rather than deep inside Npgsql on first query.
var catalogDbConnectionString = builder.Configuration.GetConnectionString("CatalogDb")
    ?? throw new InvalidOperationException(
        "Connection string 'CatalogDb' is not configured. Set ConnectionStrings__CatalogDb.");

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(
        catalogDbConnectionString,
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", CatalogDbContext.Schema)));

builder.Services.AddScoped<BookCatalog>();
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>("catalog-db");

var app = builder.Build();

// Catalog owns its schema (ADR-0004), so it brings it up to date on boot, taking a lock
// so concurrent replicas serialise rather than race (ADR-0006).
await CatalogSchema.MigrateAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapBookEndpoints();

app.Run();

// Exposed so the integration tests can boot the real application through WebApplicationFactory.
public partial class Program;
