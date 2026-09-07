using System.Net;

namespace ReadingTracker.Library.Tests;

/// <summary>
/// Stands in for the network so client code runs for real against canned payloads.
/// Tests set <see cref="Respond"/> to decide what the outside world says.
///
/// Deliberately a copy of Catalog's equivalent rather than a shared helper: two copies of
/// twenty-five lines is cheaper than a shared test package. Extract it when a third service
/// needs one.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public HttpRequestMessage? LastRequest { get; private set; }

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
