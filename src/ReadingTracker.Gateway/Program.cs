using DotNetEnv;

// Load .env before the builder reads environment variables, matching the other services so
// local development keeps its configuration in one gitignored place.
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Fail at startup with a clear message rather than proxying to nowhere on the first request.
// A Gateway silently pointed at the wrong address is worse than one that refuses to boot.
foreach (var service in DownstreamServices.All)
{
    var key = $"ReverseProxy:Clusters:{service}:Destinations:primary:Address";

    if (string.IsNullOrWhiteSpace(builder.Configuration[key]))
    {
        throw new InvalidOperationException(
            $"No address is configured for the {service} service. " +
            $"Set ReverseProxy__Clusters__{service}__Destinations__primary__Address.");
    }
}

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

var app = builder.Build();

// Answers without a token: the platform has to be able to tell whether the Gateway is up
// without holding a Google account.
app.MapHealthChecks("/health");

app.MapReverseProxy();

app.Run();

/// <summary>
/// The services the Gateway fronts. Named here only so a missing address is caught at startup;
/// which paths reach which service is configuration, not code.
/// </summary>
internal static class DownstreamServices
{
    public static readonly string[] All = ["catalog", "library"];
}

// Exposed so the integration tests can boot the real application through WebApplicationFactory.
public partial class Program;
