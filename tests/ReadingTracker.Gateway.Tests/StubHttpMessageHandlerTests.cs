using System.Net;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// The stub is shared test infrastructure rather than production code, and would not normally be
/// worth testing. It is tested here because one specific mistake in it — a stale asynchronous
/// responder left behind by an earlier test — produced a failure that depended on the order the
/// tests happened to run in, which is the kind of fault that hides for a long time and then
/// looks like something else entirely.
/// </summary>
public sealed class StubHttpMessageHandlerTests
{
    [Fact]
    public async Task Uses_the_last_answer_the_test_set_rather_than_a_leftover_one()
    {
        var stub = new StubHttpMessageHandler
        {
            RespondAsync = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
        };

        // A later test setting only the synchronous answer must not be silently overruled by the
        // asynchronous one an earlier test left behind.
        stub.Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict);

        using var client = new HttpClient(stub);
        var response = await client.GetAsync("http://anywhere.test/");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Records_every_call_rather_than_only_the_last()
    {
        var stub = new StubHttpMessageHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) };
        using var client = new HttpClient(stub);

        await client.GetAsync("http://anywhere.test/first");
        await client.GetAsync("http://anywhere.test/second");

        Assert.Equal(2, stub.RequestCount);
        Assert.Equal(
            ["/first", "/second"],
            stub.Requests.Select(request => request.RequestUri!.AbsolutePath));
        Assert.Equal("/second", stub.LastRequest!.RequestUri!.AbsolutePath);
    }
}
