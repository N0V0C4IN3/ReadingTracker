using DotNetEnv;
using Yarp.ReverseProxy.Forwarder;

// Load .env before the builder reads environment variables, matching the other services so
// local development keeps its configuration in one gitignored place.
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Fail at startup with a clear message rather than proxying to nowhere on the first request.
// A Gateway silently pointed at the wrong address is worse than one that refuses to boot.
// Read from the configured clusters rather than a list in code, so adding a service stays a
// matter of configuration.
var clusters = builder.Configuration.GetSection("ReverseProxy:Clusters").GetChildren().ToList();

if (clusters.Count == 0)
{
    throw new InvalidOperationException(
        "No services are configured behind the gateway. Expected ReverseProxy:Clusters to be populated.");
}

foreach (var destination in clusters.SelectMany(cluster => cluster.GetSection("Destinations").GetChildren()))
{
    if (string.IsNullOrWhiteSpace(destination["Address"]))
    {
        // Derived from the key itself so the message cannot drift from what it is describing.
        var setting = $"{destination.Path}:Address".Replace(":", "__");

        throw new InvalidOperationException(
            $"No address is configured for '{destination.Path}'. Set {setting}.");
    }
}

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

var app = builder.Build();

// Answers without a token: the platform has to be able to tell whether the Gateway is up
// without holding a Google account.
app.MapHealthChecks("/health");

app.MapReverseProxy(proxy =>
    proxy.Use(async (context, next) =>
    {
        await next();

        // "The service is down" and "the service said no" are different facts. Both arrive as a
        // 502, so say which one it was in the body — otherwise a downstream that itself answers
        // 502 is indistinguishable from one that could not be reached at all.
        if (context.Features.Get<IForwarderErrorFeature>() is { } failure && !context.Response.HasStarted)
        {
            var service = context.GetReverseProxyFeature().Route.Config.ClusterId ?? "a service";

            await Results.Problem(
                title: "A service behind the gateway could not be reached",
                detail: $"The gateway could not reach {service} ({failure.Error}). This is the gateway " +
                        "reporting a failure to deliver the request, not an answer from the service itself.",
                statusCode: StatusCodes.Status502BadGateway)
                .ExecuteAsync(context);
        }
    }));

app.Run();

// Exposed so the integration tests can boot the real application through WebApplicationFactory.
public partial class Program;
