using System.Net;
using System.Net.Http.Headers;

namespace ReadingTracker.Web.Services;

/// <summary>
/// The DevSignIn counterpart to <see cref="GatewayAuthorizationHandler"/>: attaches the dev
/// reader's token instead of Google's. Kept as a separate handler rather than a branch inside
/// that one so the Google path stays exactly as it was, with no dev-only code on it.
/// </summary>
public sealed class DevSignInAuthorizationHandler(DevSignInSession session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (await session.GetTokenAsync() is not { } token)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode is not HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // The Gateway generates its dev signing key fresh on every start (ADR-0008), so restarting
        // it refuses a token that has not expired and looks perfectly good from here. Mint a new
        // one for the same reader and try once, rather than leaving a developer to work out why a
        // shelf that worked a minute ago now says their session has ended.
        if (await session.RenewAsync() is not { } renewed)
        {
            return response;
        }

        response.Dispose();

        var retry = await CloneAsync(request, cancellationToken);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);

        return await base.SendAsync(retry, cancellationToken);
    }

    /// <summary>A request that has been sent cannot be sent again, so the retry needs a copy.</summary>
    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is { } content)
        {
            clone.Content = new ByteArrayContent(await content.ReadAsByteArrayAsync(cancellationToken));

            foreach (var header in content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
