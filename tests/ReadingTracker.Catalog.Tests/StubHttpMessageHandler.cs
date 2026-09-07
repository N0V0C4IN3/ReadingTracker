using System.Net;

namespace ReadingTracker.Catalog.Tests;

/// <summary>
/// Stands in for the network so provider code runs for real against canned payloads.
/// Tests set <see cref="Respond"/> to decide what the outside world says.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(Respond(request));
    }

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}
