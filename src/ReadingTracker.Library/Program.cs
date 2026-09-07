using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReadingTracker.Library.Catalog;
using ReadingTracker.Library.Entries;
using ReadingTracker.Library.Persistence;

// Load .env before the builder reads environment variables, so local development can keep
// secrets in a gitignored file. Deployed environments have no .env and supply configuration
// directly.
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.Configure<CatalogOptions>(builder.Configuration.GetSection(CatalogOptions.SectionName));

builder.Services.AddHttpClient<CatalogClient>((serviceProvider, client) =>
    client.BaseAddress = serviceProvider.GetRequiredService<IOptions<CatalogOptions>>().Value.BaseAddress);

// Fail at startup with a clear message rather than deep inside Npgsql on first query.
var libraryDbConnectionString = builder.Configuration.GetConnectionString("LibraryDb")
    ?? throw new InvalidOperationException(
        "Connection string 'LibraryDb' is not configured. Set ConnectionStrings__LibraryDb.");

builder.Services.AddDbContext<LibraryDbContext>(options =>
    options.UseNpgsql(
        libraryDbConnectionString,
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", LibraryDbContext.Schema)));

builder.Services.AddScoped<ReadingLibrary>();
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<LibraryDbContext>("library-db");

var app = builder.Build();

// Library owns its schema (ADR-0004), so it brings it up to date on boot, taking a lock so
// concurrent replicas serialise rather than race (ADR-0006).
await LibrarySchema.MigrateAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapLibraryEndpoints();

app.Run();

// Exposed so the integration tests can boot the real application through WebApplicationFactory.
public partial class Program;
