using System.Net.Http.Headers;

namespace ReadingTracker.Web.Services;

/// <summary>
/// Attaches the signed-in reader's ID token to every request to the Gateway. When there is no
/// token — signed out, or the session has ended — the request goes out with none, and the
/// Gateway's own 401 is what tells the rest of the application to say so.
/// </summary>
public sealed class GatewayAuthorizationHandler(IdTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (await tokens.GetTokenAsync() is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
