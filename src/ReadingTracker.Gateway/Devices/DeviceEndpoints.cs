using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ReadingTracker.Gateway.Persistence;

namespace ReadingTracker.Gateway.Devices;

/// <summary>
/// Where a Reader mints, sees and revokes DeviceTokens. Served by the Gateway itself rather than
/// proxied, because the Gateway owns the table and nothing behind it may know a token exists
/// (ADR-0014).
/// </summary>
public static class DeviceEndpoints
{
    /// <summary>
    /// Everything here requires a reader who signed in as a person — with Google, or with
    /// DevSignIn where that is on — never a reader standing behind a DeviceToken. Otherwise a
    /// token lifted from a lost device could mint itself successors faster than the Reader
    /// revokes them, or revoke the Reader's other devices.
    /// </summary>
    public const string InPersonPolicy = "in-person";

    /// <summary>Room for "Kindle Paperwhite (bedroom)"; a name longer than this is not a name.</summary>
    private const int MaxNameLength = 100;

    public static AuthorizationBuilder AddInPersonPolicy(this AuthorizationBuilder authorization) =>
        authorization.AddPolicy(InPersonPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(GoogleIdentity.SubjectClaim)
            .RequireAssertion(context => !context.User.HasClaim(claim => claim.Type == DeviceTokenAuthentication.TokenIdClaim)));

    public static void MapDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var devices = endpoints.MapGroup("/api/devices").RequireAuthorization(InPersonPolicy);

        devices.MapPost("/", async (
            MintRequest request,
            HttpContext http,
            GatewayDbContext db,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            var name = request.Name?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["Give the device a name."],
                });
            }

            if (name.Length > MaxNameLength)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = [$"Keep the name to {MaxNameLength} characters."],
                });
            }

            var reader = http.User.FindFirst(GoogleIdentity.SubjectClaim)!.Value;
            var secret = DeviceTokenSecret.Generate();
            var token = DeviceToken.Mint(reader, name, DeviceTokenSecret.Hash(secret), clock.GetUtcNow());

            db.DeviceTokens.Add(token);
            await db.SaveChangesAsync(cancellationToken);

            // The one and only time the secret leaves the Gateway. It is not stored, so it
            // cannot be shown again; the Reader mints another if they lose it.
            return Results.Created(
                $"/api/devices/{token.Id}",
                new MintedResponse(token.Id, token.Name, token.CreatedAt, secret));
        })
        .WithName("MintDeviceToken")
        .RequireRateLimiting(ReaderPace.MintPolicy);

        devices.MapGet("/", async (HttpContext http, GatewayDbContext db, CancellationToken cancellationToken) =>
        {
            var reader = http.User.FindFirst(GoogleIdentity.SubjectClaim)!.Value;

            var tokens = await db.DeviceTokens
                .AsNoTracking()
                .Where(t => t.ReaderId == reader)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync(cancellationToken);

            return Results.Ok(tokens.Select(ListedResponse.From));
        })
        .WithName("ListDeviceTokens");

        devices.MapDelete("/{deviceId:guid}", async (
            Guid deviceId,
            HttpContext http,
            GatewayDbContext db,
            CancellationToken cancellationToken) =>
        {
            var reader = http.User.FindFirst(GoogleIdentity.SubjectClaim)!.Value;

            // Deleted rather than flagged: the next request bearing it finds nothing, which is
            // the same answer a token that never existed gets, and there is nothing to keep it
            // for once the Reader has said it is gone.
            var revoked = await db.DeviceTokens
                .Where(t => t.Id == deviceId && t.ReaderId == reader)
                .ExecuteDeleteAsync(cancellationToken);

            // Another reader's token is "not found" — whether the id exists is not theirs to learn.
            return revoked == 0 ? Results.NotFound() : Results.NoContent();
        })
        .WithName("RevokeDeviceToken");
    }

    private sealed record MintRequest(string? Name);

    /// <summary>A token as the Devices page sees it: everything but the secret, which is not kept.</summary>
    private sealed record ListedResponse(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt)
    {
        public static ListedResponse From(DeviceToken token) =>
            new(token.Id, token.Name, token.CreatedAt, token.LastUsedAt);
    }

    /// <param name="Token">The secret, shown once.</param>
    private sealed record MintedResponse(Guid Id, string Name, DateTimeOffset CreatedAt, string Token);
}
