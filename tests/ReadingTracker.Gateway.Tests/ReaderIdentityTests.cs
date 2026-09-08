using System.Net;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// What the Gateway exists for. Nearly every assertion here is about what reached the service
/// behind it, not about what the caller got back: a test that only checked the response would
/// pass just as happily if a forged header had been forwarded untouched.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class ReaderIdentityTests(GatewayFixture fixture)
{
    private const string ReaderHeader = "X-Reader-Id";

    [Fact]
    public async Task Tells_the_service_who_the_token_says_is_asking()
    {
        var seen = fixture.ResetWithOkResponses();

        await fixture.ClientFor("google-subject-abc").GetAsync("/api/library");

        Assert.Equal("google-subject-abc", seen.Single().Headers.GetValues(ReaderHeader).Single());
    }

    [Fact]
    public async Task Turns_away_a_caller_with_no_token_at_all()
    {
        var seen = fixture.ResetWithOkResponses();

        var response = await fixture.CreateClient().GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Refused means refused: the service behind the Gateway never hears about it.
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Throws_away_a_reader_header_the_caller_made_up()
    {
        var seen = fixture.ResetWithOkResponses();
        var client = fixture.ClientFor("the-real-reader");
        client.DefaultRequestHeaders.Add(ReaderHeader, "somebody-elses-shelf");

        await client.GetAsync("/api/library");

        // The claim the caller made is gone; what arrives is what the token proved.
        Assert.Equal("the-real-reader", seen.Single().Headers.GetValues(ReaderHeader).Single());
    }

    [Fact]
    public async Task Refuses_a_reader_header_offered_without_a_token()
    {
        var seen = fixture.ResetWithOkResponses();
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add(ReaderHeader, "somebody-elses-shelf");

        var response = await client.GetAsync("/api/library");

        // Setting the header used to be enough to be anyone. It is now worth nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Turns_away_a_token_that_has_expired()
    {
        var seen = fixture.ResetWithOkResponses();
        var expired = fixture.Google.Token(expires: DateTime.UtcNow.AddMinutes(-5));

        var response = await fixture.ClientWithToken(expired).GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Turns_away_a_token_signed_by_someone_other_than_google()
    {
        var seen = fixture.ResetWithOkResponses();
        // Correctly formed, correct issuer and audience, plausible in every way except that
        // Google did not sign it.
        var forged = fixture.Google.Token(signedWith: FakeGoogle.UntrustedKey());

        var response = await fixture.ClientWithToken(forged).GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Turns_away_a_real_google_token_that_was_issued_to_a_different_app()
    {
        var seen = fixture.ResetWithOkResponses();
        // Genuinely signed by Google, for somebody else's application. Checking the signature
        // without checking the audience would let every Google app in the world sign in here.
        var elsewhere = fixture.Google.Token(audience: "some-other-app.apps.googleusercontent.com");

        var response = await fixture.ClientWithToken(elsewhere).GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Turns_away_a_token_that_claims_to_come_from_somewhere_else()
    {
        var seen = fixture.ResetWithOkResponses();

        var response = await fixture
            .ClientWithToken(fixture.Google.Token(issuer: "https://accounts.not-google.test"))
            .GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Turns_away_a_token_that_does_not_say_who_the_reader_is()
    {
        var seen = fixture.ResetWithOkResponses();

        var response = await fixture
            .ClientWithToken(fixture.Google.TokenWithoutSubject())
            .GetAsync("/api/library");

        // Forbidden rather than Unauthorized, and the distinction is real: Google vouched for
        // this token, it simply does not say who the reader is. Forwarding it would mean a
        // request with no reader, and the Gateway must not be the thing that invented one.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Does_not_tell_a_stranger_which_paths_exist()
    {
        var seen = fixture.ResetWithOkResponses();

        var stranger = await fixture.CreateClient().GetAsync("/api/nonsense");
        var reader = await fixture.ClientFor("browsing-reader").GetAsync("/api/nonsense");

        // A caller with no token is turned away before learning anything, so the Gateway cannot
        // be used to map out which routes are real. A signed-in reader gets the honest answer.
        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reader.StatusCode);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Keeps_two_readers_apart()
    {
        var seen = fixture.ResetWithOkResponses();

        await fixture.ClientFor("reader-one").GetAsync("/api/library");
        await fixture.ClientFor("reader-two").GetAsync("/api/library");

        Assert.Equal(
            ["reader-one", "reader-two"],
            seen.Select(request => request.Headers.GetValues(ReaderHeader).Single()));
    }

    [Fact]
    public async Task Guards_the_catalog_too_not_just_the_library()
    {
        var seen = fixture.ResetWithOkResponses();

        var refused = await fixture.CreateClient().GetAsync("/api/books/search?isbn=9780441013593");
        await fixture.ClientFor("catalog-reader").GetAsync("/api/books/search?isbn=9780441013593");

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("catalog-reader", seen.Single().Headers.GetValues(ReaderHeader).Single());
    }

    [Fact]
    public async Task Still_answers_a_health_check_to_someone_with_no_token()
    {
        var response = await fixture.CreateClient().GetAsync("/health");

        // The platform has to be able to ask whether the Gateway is up without a Google account.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Asks_google_for_its_signing_keys_once_rather_than_on_every_request()
    {
        fixture.ResetWithOkResponses();
        await fixture.ClientFor("warm-up").GetAsync("/api/library");
        var fetchesBefore = fixture.Google.KeyFetches;

        for (var i = 0; i < 5; i++)
        {
            await fixture.ClientFor($"reader-{i}").GetAsync("/api/library");
        }

        // Five requests, no further trips to Google: an outage or a rate limit at Google must not
        // take the whole application down request by request.
        Assert.Equal(fetchesBefore, fixture.Google.KeyFetches);
    }

    // Surviving a key rotation is deliberately not tested here. Recovery works by refetching
    // Google's keys when a token turns up signed by one that isn't cached, and that refetch is
    // throttled to at most one every thirty seconds — a rate limit worth having, since otherwise
    // a stream of junk tokens would turn into a stream of requests to Google. Waiting out that
    // window would add thirty seconds to a suite that currently runs in one. The rejected
    // alternative was a test that rotates the key and immediately presents a token signed with
    // it: that fails, but only because it simulates something Google does not do — new keys are
    // published well before they are used to sign, precisely so clients keep working.

    [Fact]
    public async Task Says_how_to_authenticate_when_it_turns_someone_away()
    {
        var response = await fixture.CreateClient().GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            response.Headers.WwwAuthenticate,
            header => header.Scheme == "Bearer");
    }

    [Fact]
    public async Task Does_not_leak_a_token_onward_to_the_services_behind_it()
    {
        var seen = fixture.ResetWithOkResponses();

        await fixture.ClientFor("private-reader").GetAsync("/api/library");

        // The services behind the Gateway have no use for the token and no business holding it.
        // The reader's identity is the only thing they are told.
        Assert.DoesNotContain(seen.Single().Headers, header =>
            header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
    }
}
