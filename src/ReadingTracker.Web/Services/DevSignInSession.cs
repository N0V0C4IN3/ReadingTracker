using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace ReadingTracker.Web.Services;

/// <summary>
/// Whether this build signs readers in through the Gateway's dev endpoint instead of Google.
/// </summary>
public sealed class DevSignInOptions
{
    public const string SectionName = "DevSignIn";

    /// <summary>
    /// False in the committed wwwroot/appsettings.json, and true only in
    /// wwwroot/appsettings.Development.json — which a browser only ever fetches when the app is
    /// served in the Development environment. See ADR-0009.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>The reader the sign-in box starts out offering to be.</summary>
    public string DefaultReaderId { get; set; } = "dev-reader";
}

/// <summary>
/// The dev reader identity this application is holding, and the Gateway call that mints one.
/// Stands in for the whole Google OIDC session when <see cref="DevSignInOptions.Enabled"/> is on
/// — see ADR-0009 for why the two are alternatives rather than both being available at once.
/// </summary>
public sealed class DevSignInSession(HttpClient httpClient, IJSRuntime jsRuntime)
{
    // Kept in localStorage so a reload — or the redirect-free equivalent of one, a hard refresh
    // while poking at the app — does not mean signing in again on every page.
    private const string StorageKey = "readingtracker.dev-id-token";

    /// <summary>Who the held token says the reader is, or null when there is no token.</summary>
    public string? ReaderId { get; private set; }

    /// <summary>
    /// Null when nobody is signed in.
    ///
    /// Read straight out of storage every time rather than cached in this instance. That is not
    /// belt-and-braces: IHttpClientFactory builds a handler chain once and reuses it for minutes,
    /// so a session captured inside <see cref="DevSignInAuthorizationHandler"/> outlives any one
    /// sign-in. Cached here, signing in as a second reader would keep sending the first reader's
    /// token — and quietly show one reader another's shelf. Storage is the only thing that knows.
    /// </summary>
    public async Task<string?> GetTokenAsync()
    {
        var token = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        Hold(token);

        return token;
    }

    /// <summary>True when the Gateway minted a token for <paramref name="readerId"/>.</summary>
    public async Task<bool> SignInAsync(string readerId) =>
        await MintAsync(readerId) is not null;

    /// <summary>
    /// Mints a replacement token for whoever is currently signed in. The Gateway generates its
    /// dev signing key fresh on every start (ADR-0008), so a token held across a Gateway restart
    /// is refused despite not having expired; this is how that is recovered from.
    /// </summary>
    public async Task<string?> RenewAsync()
    {
        // Read through rather than trusting ReaderId, for the same reason GetTokenAsync does.
        await GetTokenAsync();

        // Nobody is signed in, so there is nobody to renew for. Signing in is a deliberate act,
        // never something a refused request does on a reader's behalf.
        return ReaderId is { } readerId ? await MintAsync(readerId) : null;
    }

    public async Task SignOutAsync()
    {
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        Hold(null);
    }

    private async Task<string?> MintAsync(string readerId)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.PostAsJsonAsync("dev/sign-in", new { readerId });
        }
        catch (HttpRequestException)
        {
            return null;
        }

        // A Gateway outside Development, or with DevSignIn switched off, does not map this
        // endpoint at all: a 404 here means the two halves disagree about being in dev mode,
        // which is a setup problem rather than a rejected sign-in.
        if (!response.IsSuccessStatusCode ||
            await response.Content.ReadFromJsonAsync<MintedToken>() is not { IdToken: { } token })
        {
            return null;
        }

        await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, token);
        Hold(token);

        return token;
    }

    private void Hold(string? token) =>
        ReaderId = ReaderIdOf(token);

    /// <summary>
    /// The reader id inside a token, read straight out of the payload without verifying anything.
    /// That is not a shortcut: the Gateway verifies the token on every single request, and this
    /// value is only ever used to put a name on the screen.
    /// </summary>
    private static string? ReaderIdOf(string? token)
    {
        if (token?.Split('.') is not { Length: 3 } parts)
        {
            return null;
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));

            return document.RootElement.TryGetProperty("sub", out var subject)
                ? subject.GetString()
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private sealed record MintedToken(string? IdToken);
}
