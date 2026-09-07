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

    /// <summary>How many times the provider has actually been reached over the network.</summary>
    public int RequestCount => _requestCount;

    private int _requestCount;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        LastRequest = request;
        return Task.FromResult(Respond(request));
    }

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}
