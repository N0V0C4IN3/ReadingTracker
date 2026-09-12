using System.Net;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public sealed class DevSignInSessionTests
{
    private const string StorageKey = "readingtracker.dev-id-token";

    [Fact]
    public async Task Signs_in_by_asking_the_gateway_to_mint_a_token()
    {
        var (session, gateway, storage) = CreateSession();
        gateway.Respond = _ => Minted("ada");

        Assert.True(await session.SignInAsync("ada"));

        Assert.Equal("/dev/sign-in", gateway.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("ada", session.ReaderId);
        Assert.Equal(DevToken.For("ada"), storage[StorageKey]);
    }

    [Fact]
    public async Task Sends_the_reader_id_the_caller_asked_to_be()
    {
        var (session, gateway, _) = CreateSession();
        string? body = null;

        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Minted("grace");
        };

        await session.SignInAsync("grace");

        Assert.Contains("\"readerId\":\"grace\"", body);
    }

    [Fact]
    public async Task Restores_a_token_kept_from_an_earlier_page_load()
    {
        var (session, gateway, storage) = CreateSession();
        storage.Seed(StorageKey, DevToken.For("ada"));

        Assert.Equal(DevToken.For("ada"), await session.GetTokenAsync());
        Assert.Equal("ada", session.ReaderId);

        // Reading a token already in storage is not a reason to go and mint another one.
        Assert.Equal(0, gateway.RequestCount);
    }

    [Fact]
    public async Task Holds_nobody_when_nothing_was_kept()
    {
        var (session, _, _) = CreateSession();

        Assert.Null(await session.GetTokenAsync());
        Assert.Null(session.ReaderId);
    }

    [Fact]
    public async Task Reports_a_gateway_that_will_not_mint()
    {
        var (session, gateway, storage) = CreateSession();

        // What a Gateway outside Development, or with DevSignIn off, actually does: the endpoint
        // is never mapped, so the call 404s rather than being refused.
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        Assert.False(await session.SignInAsync("ada"));
        Assert.Null(await session.GetTokenAsync());
        Assert.Null(storage[StorageKey]);
    }

    [Fact]
    public async Task Reports_a_gateway_that_cannot_be_reached_at_all()
    {
        var (session, gateway, _) = CreateSession();
        gateway.Respond = _ => throw new HttpRequestException("connection refused");

        Assert.False(await session.SignInAsync("ada"));
        Assert.Null(await session.GetTokenAsync());
    }

    [Fact]
    public async Task Signing_out_forgets_the_token()
    {
        var (session, gateway, storage) = CreateSession();
        gateway.Respond = _ => Minted("ada");
        await session.SignInAsync("ada");

        await session.SignOutAsync();

        Assert.Null(await session.GetTokenAsync());
        Assert.Null(session.ReaderId);
        Assert.Null(storage[StorageKey]);
    }

    [Fact]
    public async Task Renewing_mints_a_fresh_token_for_the_same_reader()
    {
        var (session, gateway, _) = CreateSession();
        gateway.Respond = _ => Minted("ada");
        await session.SignInAsync("ada");

        string? body = null;

        gateway.RespondAsync = async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Minted("ada");
        };

        Assert.NotNull(await session.RenewAsync());
        Assert.Contains("\"readerId\":\"ada\"", body);
    }

    [Fact]
    public async Task Renewing_signs_nobody_in_when_nobody_was()
    {
        var (session, gateway, _) = CreateSession();
        gateway.Respond = _ => Minted("dev-reader");

        Assert.Null(await session.RenewAsync());

        // The Gateway defaults a missing reader id to "dev-reader", so asking it to renew for
        // nobody would sign in a reader who never asked to be signed in.
        Assert.Equal(0, gateway.RequestCount);
    }

    [Fact]
    public async Task Ignores_a_stored_value_that_is_not_a_token()
    {
        var (session, _, storage) = CreateSession();
        storage.Seed(StorageKey, "not-a-jwt");

        // The token is still handed over — the Gateway is the thing that judges it — but nothing
        // is invented about who the reader is.
        Assert.Equal("not-a-jwt", await session.GetTokenAsync());
        Assert.Null(session.ReaderId);
    }

    private static (DevSignInSession Session, StubHttpMessageHandler Gateway, FakeLocalStorage Storage)
        CreateSession()
    {
        var gateway = new StubHttpMessageHandler();
        var storage = new FakeLocalStorage();
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };

        return (new DevSignInSession(httpClient, storage), gateway, storage);
    }

    private static HttpResponseMessage Minted(string readerId) =>
        StubHttpMessageHandler.Json($$"""{ "idToken": "{{DevToken.For(readerId)}}" }""");
}
