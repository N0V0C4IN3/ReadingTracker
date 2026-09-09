using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace ReadingTracker.Web.Services;

/// <summary>
/// Reports the dev reader as the signed-in one, in place of the Google OIDC session. Registered
/// only when DevSignIn is enabled (ADR-0009); the Google path never sees this type.
/// </summary>
public sealed class DevAuthenticationStateProvider(DevSignInSession session) : AuthenticationStateProvider
{
    private static readonly AuthenticationState SignedOut = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (await session.GetTokenAsync() is null || session.ReaderId is not { } readerId)
        {
            return SignedOut;
        }

        // The same two claims the Gateway puts in the token it minted, so what the page shows and
        // what the Gateway will act on cannot drift apart.
        var identity = new ClaimsIdentity(
            [new Claim("sub", readerId), new Claim("name", $"Dev Reader ({readerId})")],
            authenticationType: "DevSignIn",
            nameType: "name",
            roleType: "role");

        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>True when the Gateway minted a token; false when it refused or was unreachable.</summary>
    public async Task<bool> SignInAsync(string readerId)
    {
        var signedIn = await session.SignInAsync(readerId);

        if (signedIn)
        {
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        return signedIn;
    }

    public async Task SignOutAsync()
    {
        await session.SignOutAsync();
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
