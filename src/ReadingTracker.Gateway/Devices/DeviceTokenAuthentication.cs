using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ReadingTracker.Gateway.Persistence;

namespace ReadingTracker.Gateway.Devices;

/// <summary>
/// The second way into the Gateway (ADR-0014). A bearer token shaped like a DeviceToken is
/// looked up rather than verified: the Gateway minted it, so the Gateway is the only thing that
/// can say whether it is real. Everything downstream of authentication is shared with the Google
/// path — the same subject claim, the same reader header, the same pace — so nothing behind the
/// Gateway can tell, or needs to, how a reader signed in.
/// </summary>
public static class DeviceTokenAuthentication
{
    public const string Scheme = "DeviceToken";

    /// <summary>
    /// The scheme every request is authenticated under: it looks at the token and hands it to
    /// the DeviceToken check or to Google's, whichever the shape says.
    /// </summary>
    public const string SelectorScheme = "ReaderIdentity";

    /// <summary>
    /// Present on a principal that arrived by DeviceToken and on no other. What a policy checks
    /// to keep a device from minting or revoking DeviceTokens.
    /// </summary>
    public const string TokenIdClaim = "device_token";

    public static AuthenticationBuilder AddDeviceTokens(this AuthenticationBuilder authentication) =>
        authentication
            .AddPolicyScheme(SelectorScheme, SelectorScheme, options =>
                options.ForwardDefaultSelector = http =>
                    DeviceTokenSecret.IsShapedLike(BearerTokenOf(http))
                        ? Scheme
                        : JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, Handler>(Scheme, _ => { });

    private static string? BearerTokenOf(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }

    private sealed class Handler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        GatewayDbContext db,
        TimeProvider clock)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (BearerTokenOf(Context) is not { } secret || !DeviceTokenSecret.IsShapedLike(secret))
            {
                return AuthenticateResult.NoResult();
            }

            var hash = DeviceTokenSecret.Hash(secret);

            var token = await db.DeviceTokens
                .SingleOrDefaultAsync(t => t.SecretHash == hash, Context.RequestAborted);

            if (token is null)
            {
                // Unknown and revoked look the same from here, on purpose: a revoked token is
                // deleted, and telling a caller which of the two they hold helps only an attacker.
                return AuthenticateResult.Fail("This DeviceToken is not known to the Gateway.");
            }

            // So the Devices page can show a token that has gone quiet. Coarse, so that a device
            // in use is one write a minute rather than one a request.
            if (token.Touch(clock.GetUtcNow()))
            {
                await db.SaveChangesAsync(Context.RequestAborted);
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(GoogleIdentity.SubjectClaim, token.ReaderId),
                    new Claim(TokenIdClaim, token.Id.ToString()),
                ],
                DeviceTokenAuthentication.Scheme);

            return AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), DeviceTokenAuthentication.Scheme));
        }

        /// <summary>
        /// The same answer Google's handler gives: a bearer token is what was wanted. A device
        /// that sent a bad one is told how to authenticate, not that its token was revoked.
        /// </summary>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.Append(HeaderNames.WWWAuthenticate, "Bearer");
            return Task.CompletedTask;
        }
    }
}
