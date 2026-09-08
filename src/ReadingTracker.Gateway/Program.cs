using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ReadingTracker.Gateway;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

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

// Without this the Gateway would accept tokens issued to any Google application at all, so a
// missing value is a security hole rather than an inconvenience. Refuse to start.
var googleClientId = builder.Configuration["Google:ClientId"]
    ?? throw new InvalidOperationException(
        "No Google client id is configured, so tokens could not be checked against this " +
        "application. Set Google__ClientId.");

// The browser calls the Gateway across origins, so this is required for the frontend to work at
// all — but a wildcard would let any site on the internet ride a signed-in reader's browser to
// make requests here. Named origins only, and a missing setting fails startup rather than
// silently accepting everything or nothing.
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();

if (allowedOrigins is not { Length: > 0 })
{
    throw new InvalidOperationException(
        "No AllowedOrigins are configured, so no browser could call the gateway. Set AllowedOrigins__0.");
}

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Google publishes its signing keys; the handler fetches them through this authority and
        // caches them rather than asking per request, so a slow or rate-limiting Google does not
        // become a slow or failing application.
        options.Authority = GoogleIdentity.Issuer;

        // When a token arrives signed by a key that isn't cached, go and look again — this is
        // what carries the Gateway through Google's key rotations. The refetch is throttled
        // internally, which is what stops a stream of junk tokens becoming a stream of requests
        // to Google.
        options.RefreshOnIssuerKeyNotFound = true;

        // Keep Google's own claim names rather than translating them into the older SOAP-era
        // URIs, so "sub" in the token is "sub" in the code.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,

            // Google issues tokens under both spellings and treats them as equivalent.
            ValidIssuers = [GoogleIdentity.Issuer, "accounts.google.com"],

            // The signature only proves Google minted it. Without this, a token issued to any
            // other Google application in the world would be accepted here.
            ValidateAudience = true,
            ValidAudience = googleClientId,

            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        // A token with no subject names no reader, and the Gateway must not be the thing that
        // invents one.
        .RequireClaim(GoogleIdentity.SubjectClaim)
        .Build());

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context => context.AddRequestTransform(transform =>
    {
        // Unconditionally, before anything else: whatever the caller claimed about who they are
        // is discarded. ADR-0007 makes this the Gateway's job, and it is the whole difference
        // between a trusted-gateway topology and an open one.
        transform.ProxyRequest.Headers.Remove(GoogleIdentity.ReaderHeader);

        // The token was for us to check, not for the services behind us to hold.
        transform.ProxyRequest.Headers.Authorization = null;

        if (transform.HttpContext.User.FindFirst(GoogleIdentity.SubjectClaim)?.Value is { } reader)
        {
            transform.ProxyRequest.Headers.Add(GoogleIdentity.ReaderHeader, reader);
        }

        return ValueTask.CompletedTask;
    }));

builder.Services.AddHealthChecks();

var app = builder.Build();

// Answers without a token: the platform has to be able to tell whether the Gateway is up
// without holding a Google account.
app.MapHealthChecks("/health").AllowAnonymous();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

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
