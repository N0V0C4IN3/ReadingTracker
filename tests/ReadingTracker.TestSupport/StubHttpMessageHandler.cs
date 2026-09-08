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
    /// <summary>
    /// What the outside world says. Setting this clears <see cref="RespondAsync"/>, so the last
    /// answer a test sets is the one that is used — otherwise a stale asynchronous responder left
    /// behind by an earlier test would silently win over this one.
    /// </summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Respond
    {
        get => _respond;
        set
        {
            _respond = value;
            _respondAsync = null;
        }
    }

    /// <summary>
    /// Set this instead of <see cref="Respond"/> when the test needs to read the request body.
    /// A proxied body is streamed from the live request rather than buffered, so it can only be
    /// read while the call is still in flight — by the time the caller has its response, the
    /// stream is gone. Setting this clears <see cref="Respond"/> for the same reason.
    /// </summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? RespondAsync
    {
        get => _respondAsync;
        set => _respondAsync = value;
    }

    private Func<HttpRequestMessage, HttpResponseMessage> _respond =
        _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _respondAsync;

    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>
    /// Every call made through this handler, so a test can assert not just what the code under
    /// test asked for but what it never asked for.
    /// </summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>How many times the outside world has actually been reached.</summary>
    public int RequestCount
    {
        get
        {
            lock (Requests)
            {
                return Requests.Count;
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // One lock over everything recorded, so a caller can never see a request counted but not
        // yet listed.
        lock (Requests)
        {
            Requests.Add(request);
            LastRequest = request;
        }

        return _respondAsync is { } asynchronously
            ? asynchronously(request, cancellationToken)
            : Task.FromResult(_respond(request));
    }

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
