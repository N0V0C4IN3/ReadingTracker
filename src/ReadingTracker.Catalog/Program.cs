using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReadingTracker.Catalog.Books;
using ReadingTracker.Catalog.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.Configure<GoogleBooksOptions>(
    builder.Configuration.GetSection(GoogleBooksOptions.SectionName));

builder.Services.AddHttpClient<IBookProvider, GoogleBooksProvider>((serviceProvider, client) =>
    client.BaseAddress = serviceProvider.GetRequiredService<IOptions<GoogleBooksOptions>>().Value.BaseAddress);

// Fail at startup with a clear message rather than deep inside Npgsql on first query.
var catalogDbConnectionString = builder.Configuration.GetConnectionString("CatalogDb")
    ?? throw new InvalidOperationException(
        "Connection string 'CatalogDb' is not configured. Set ConnectionStrings__CatalogDb.");

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(
        catalogDbConnectionString,
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", CatalogDbContext.Schema)));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>("catalog-db");

var app = builder.Build();

// Catalog owns its schema (ADR-0004), so it brings it up to date on boot.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapBookEndpoints();

app.Run();

// Exposed so the integration tests can boot the real application through WebApplicationFactory.
public partial class Program;
