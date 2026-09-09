using System.Net;
using System.Net.Http.Json;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public sealed class DevSignInAuthorizationHandlerTests
{
    private const string StorageKey = "readingtracker.dev-id-token";

    [Fact]
    public async Task Attaches_the_dev_readers_token_to_every_gateway_request()
    {
        var (client, gateway, mint, storage) = CreateClient();
        storage.Seed(StorageKey, DevToken.For("ada"));
        gateway.Respond = _ => StubHttpMessageHandler.Json("[]");

        await client.GetAsync("api/library");

        Assert.Equal("Bearer", gateway.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal(DevToken.For("ada"), gateway.LastRequest.Headers.Authorization.Parameter);
        Assert.Equal(0, mint.RequestCount);
    }

    [Fact]
    public async Task Sends_no_authorization_at_all_when_nobody_is_signed_in()
    {
        var (client, gateway, _, _) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        await client.GetAsync("api/library");

        Assert.Null(gateway.LastRequest!.Headers.Authorization);

        // Nothing to renew, so nothing is retried: one request, and the 401 stands.
        Assert.Equal(1, gateway.RequestCount);
    }

    [Fact]
    public async Task Mints_a_replacement_when_the_gateway_no_longer_recognises_the_token()
    {
        // Exactly what a Gateway restart looks like from here: the held token has not expired and
        // is perfectly well formed, but the key it was signed with no longer exists (ADR-0008).
        var (client, gateway, mint, storage) = CreateClient();
        storage.Seed(StorageKey, DevToken.For("ada", nonce: "before-restart"));

        mint.Respond = _ => StubHttpMessageHandler.Json(
            $$"""{ "idToken": "{{DevToken.For("ada", nonce: "after-restart")}}" }""");

        gateway.Respond = request => request.Headers.Authorization!.Parameter == DevToken.For("ada", "after-restart")
            ? StubHttpMessageHandler.Json("""[{"title":"a book"}]""")
            : new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var response = await client.GetAsync("api/library");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""[{"title":"a book"}]""", await response.Content.ReadAsStringAsync());

        // Once refused, once renewed, once accepted — not a retry loop.
        Assert.Equal(2, gateway.RequestCount);
        Assert.Equal(1, mint.RequestCount);
    }

    [Fact]
    public async Task Carries_the_body_over_to_the_retried_request()
    {
        var (client, gateway, mint, storage) = CreateClient();
        storage.Seed(StorageKey, DevToken.For("ada", nonce: "before-restart"));

        mint.Respond = _ => StubHttpMessageHandler.Json(
            $$"""{ "idToken": "{{DevToken.For("ada", nonce: "after-restart")}}" }""");

        var bodies = new List<string>();

        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

            return request.Headers.Authorization!.Parameter == DevToken.For("ada", "after-restart")
                ? StubHttpMessageHandler.Json("""{"id":"11111111-1111-1111-1111-111111111111"}""")
                : new HttpResponseMessage(HttpStatusCode.Unauthorized);
        };

        var bookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var response = await client.PostAsJsonAsync("api/library", new { bookId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Adding a book must not become adding nothing just because the first attempt was refused.
        Assert.Equal(2, bodies.Count);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Contains(bookId.ToString(), bodies[1]);
    }

    [Fact]
    public async Task Lets_the_refusal_stand_when_no_replacement_can_be_minted()
    {
        var (client, gateway, mint, storage) = CreateClient();
        storage.Seed(StorageKey, DevToken.For("ada"));
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        // A Gateway that is no longer in dev mode does not map the endpoint at all.
        mint.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var response = await client.GetAsync("api/library");

        // The reader really is not signed in, and the page should say so rather than spin. With no
        // replacement token there is nothing to retry with, so the Gateway is asked exactly once.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, gateway.RequestCount);
        Assert.Equal(1, mint.RequestCount);
    }

    [Fact]
    public async Task Sends_the_reader_who_is_signed_in_now_not_the_one_who_was()
    {
        // IHttpClientFactory builds a handler chain once and reuses it for minutes, so this handler
        // — and the session inside it — outlives any one sign-in. A session that cached the token
        // in memory would keep sending the first reader's, and show the second reader the first
        // one's shelf. Nothing about that failure looks like an authentication bug from the UI.
        var (client, gateway, mint, storage) = CreateClient();
        storage.Seed(StorageKey, DevToken.For("dev-reader"));
        gateway.Respond = _ => StubHttpMessageHandler.Json("[]");

        await client.GetAsync("api/library");
        Assert.Equal(DevToken.For("dev-reader"), gateway.LastRequest!.Headers.Authorization!.Parameter);

        // Signing in as somebody else replaces what is in storage, through a different instance
        // than the one this long-lived handler is holding.
        var elsewhere = new DevSignInSession(
            new HttpClient(mint) { BaseAddress = new Uri("http://gateway.test/") },
            storage);

        mint.Respond = _ => StubHttpMessageHandler.Json($$"""{ "idToken": "{{DevToken.For("ada")}}" }""");
        Assert.True(await elsewhere.SignInAsync("ada"));

        await client.GetAsync("api/library");

        Assert.Equal(DevToken.For("ada"), gateway.LastRequest!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Stops_sending_a_token_once_the_reader_signs_out()
    {
        var (client, gateway, mint, storage) = CreateClient();
        storage.Seed(StorageKey, DevToken.For("ada"));
        gateway.Respond = _ => StubHttpMessageHandler.Json("[]");

        await client.GetAsync("api/library");
        Assert.NotNull(gateway.LastRequest!.Headers.Authorization);

        var elsewhere = new DevSignInSession(
            new HttpClient(mint) { BaseAddress = new Uri("http://gateway.test/") },
            storage);

        await elsewhere.SignOutAsync();

        await client.GetAsync("api/library");

        Assert.Null(gateway.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task Leaves_a_refusal_that_is_not_about_the_token_alone()
    {
        var (client, gateway, mint, storage) = CreateClient();
        storage.Seed(StorageKey, DevToken.For("ada"));
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict);

        var response = await client.GetAsync("api/library");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, gateway.RequestCount);
        Assert.Equal(0, mint.RequestCount);
    }

    private static (HttpClient Client, StubHttpMessageHandler Gateway, StubHttpMessageHandler Mint, FakeLocalStorage Storage)
        CreateClient()
    {
        // Two stubs, because these really are two different journeys: the API calls go out through
        // the handler under test, while minting a token deliberately does not.
        var gateway = new StubHttpMessageHandler();
        var mint = new StubHttpMessageHandler();
        var storage = new FakeLocalStorage();

        var session = new DevSignInSession(
            new HttpClient(mint) { BaseAddress = new Uri("http://gateway.test/") },
            storage);

        var handler = new DevSignInAuthorizationHandler(session) { InnerHandler = gateway };

        return (new HttpClient(handler) { BaseAddress = new Uri("http://gateway.test/") }, gateway, mint, storage);
    }
}
