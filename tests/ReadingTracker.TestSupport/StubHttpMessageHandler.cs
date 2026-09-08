using System.Net;
using System.Text;

namespace ReadingTracker.TestSupport;

/// <summary>
/// Stands in for the network so the code under test runs for real against canned payloads.
/// Tests set <see cref="Respond"/> to decide what the outside world says.
///
/// Stubbing here rather than by replacing a client class is deliberate: everything between the
/// service and the socket — request building, serialisation, status handling, response parsing —
/// still executes, so the tests exercise the code that actually ships.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    /// <summary>
    /// Set this instead of <see cref="Respond"/> when the test needs to read the request body.
    /// A proxied body is streamed from the live request rather than buffered, so it can only be
    /// read while the call is still in flight — by the time the caller has its response, the
    /// stream is gone.
    /// </summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? RespondAsync { get; set; }

    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>
    /// Every call made through this handler, so a test can assert not just what the code under
    /// test asked for but what it never asked for.
    /// </summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>How many times the outside world has actually been reached.</summary>
    public int RequestCount => _requestCount;

    private int _requestCount;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        LastRequest = request;

        lock (Requests)
        {
            Requests.Add(request);
        }

        return RespondAsync is { } asynchronously
            ? asynchronously(request, cancellationToken)
            : Task.FromResult(Respond(request));
    }

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
