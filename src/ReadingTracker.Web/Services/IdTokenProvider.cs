using Microsoft.JSInterop;

namespace ReadingTracker.Web.Services;

/// <summary>
/// The one token this application actually sends: the ID token the Gateway validates
/// (ADR-0001). See wwwroot/js/idToken.js for why this needs its own JS interop rather than
/// the framework's built-in access-token provider.
/// </summary>
public sealed class IdTokenProvider(IJSRuntime jsRuntime)
{
    private IJSObjectReference? _module;

    /// <summary>Null when there is no signed-in reader, or their session has ended.</summary>
    public async Task<string?> GetTokenAsync()
    {
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/idToken.js");
        return await _module.InvokeAsync<string?>("getIdToken");
    }
}
