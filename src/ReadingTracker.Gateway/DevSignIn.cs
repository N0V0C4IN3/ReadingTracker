using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ReadingTracker.Gateway;

/// <summary>
/// Lets a developer (or an agent working on this codebase, which must never touch a real
/// reader's Google credentials) get a valid, Gateway-verified reader identity without a real
/// Google account. Only wired up when <see cref="Program"/> has decided DevSignIn is enabled —
/// see ADR-0008 for the two independent gates that decides.
/// </summary>
public static class DevSignIn
{
    public const string Issuer = "reading-tracker-dev";

    public static IEndpointConventionBuilder MapDevSignIn(
        this IEndpointRouteBuilder endpoints,
        SymmetricSecurityKey signingKey,
        string audience) =>
        endpoints.MapPost("/dev/sign-in", (DevSignInRequest? body) =>
        {
            var readerId = string.IsNullOrWhiteSpace(body?.ReaderId) ? "dev-reader" : body.ReaderId.Trim();

            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = audience,
                Expires = DateTime.UtcNow.AddHours(8),
                Claims = new Dictionary<string, object>
                {
                    [GoogleIdentity.SubjectClaim] = readerId,
                    ["name"] = $"Dev Reader ({readerId})",
                },
                SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
            });

            return Results.Ok(new DevSignInResponse(token));
        })
        .WithName("DevSignIn")
        .AllowAnonymous();

    private sealed record DevSignInRequest(string? ReaderId);

    private sealed record DevSignInResponse(string IdToken);
}
