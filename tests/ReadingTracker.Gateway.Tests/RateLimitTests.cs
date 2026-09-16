using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// Nothing behind the Gateway can tell a reader from a loop: a signed-in reader — anyone with a
/// Google account — could search until the Books quota was gone for everyone, or add Books by
/// hand until the shared Catalog was full of them. The Gateway knows who is asking (ADR-0007),
/// so it is where each reader's pace is bounded.
///
/// Its own fixture, with limits small enough to reach in a test: the shared one runs at the
/// real defaults, which the rest of the suite must never get anywhere near.
/// </summary>
public sealed class RateLimitTests : IClassFixture<RateLimitTests.Fixture>
{
    private readonly Fixture _fixture;

    public RateLimitTests(Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Downstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Turns_a_reader_away_once_they_have_searched_enough_for_one_minute()
    {
        var reader = _fixture.ClientFor("searches-a-lot");

        for (var n = 1; n <= Fixture.SearchesPerMinute; n++)
        {
            Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/books/search?q=dune")).StatusCode);
        }

        var refused = await reader.GetAsync("/api/books/search?q=dune");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter is { Delta: { } wait } && wait > TimeSpan.Zero, "Retry-After says how long");
        // Their pace, not everyone's: the next reader is not paying for it.
        Assert.Equal(
            HttpStatusCode.OK,
            (await _fixture.ClientFor("searches-once").GetAsync("/api/books/search?q=dune")).StatusCode);
    }

    [Fact]
    public async Task Bounds_adding_by_hand_separately_and_more_tightly()
    {
        var reader = _fixture.ClientFor("adds-a-lot");
        var book = new { title = "By Hand", authors = new[] { "A. Writer" } };

        for (var n = 1; n <= Fixture.BooksByHandPerMinute; n++)
        {
            Assert.Equal(HttpStatusCode.OK, (await reader.PostAsJsonAsync("/api/books", book)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await reader.PostAsJsonAsync("/api/books", book)).StatusCode);
        // Out of hand-entered Books, not out of searches.
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/books/search?q=dune")).StatusCode);
    }

    [Fact]
    public async Task Bounds_callers_with_no_reader_by_address_without_touching_any_readers_allowance()
    {
        var nobody = _fixture.CreateClient();

        for (var n = 1; n <= Fixture.AnonymousRequestsPerMinute; n++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await nobody.GetAsync("/api/library")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await nobody.GetAsync("/api/library")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await _fixture.ClientFor("unbothered").GetAsync("/api/library")).StatusCode);
    }

    public sealed class Fixture : WebApplicationFactory<Program>
    {
        public const int SearchesPerMinute = 3;

        public const int BooksByHandPerMinute = 2;

        public const int AnonymousRequestsPerMinute = 4;

        public StubHttpMessageHandler Downstream { get; } = new();

        private readonly FakeGoogle _google = new();

        private readonly StubHttpMessageHandler _googleTransport = new();

        public HttpClient ClientFor(string subject)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", _google.Token(subject));
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ReverseProxy:Clusters:catalog:Destinations:primary:Address", "http://catalog.test/");
            builder.UseSetting("ReverseProxy:Clusters:library:Destinations:primary:Address", "http://library.test/");
            builder.UseSetting("Google:ClientId", FakeGoogle.ClientId);
            builder.UseSetting("AllowedOrigins:0", "http://frontend.test");
            builder.UseSetting("RateLimits:SearchesPerMinute", SearchesPerMinute.ToString());
            builder.UseSetting("RateLimits:BooksByHandPerMinute", BooksByHandPerMinute.ToString());
            builder.UseSetting("RateLimits:AnonymousRequestsPerMinute", AnonymousRequestsPerMinute.ToString());

            _googleTransport.Respond = _google.Answer;

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IForwarderHttpClientFactory>(new StubForwarder(Downstream));
                services.Configure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options => options.Backchannel = new HttpClient(_googleTransport));
            });
        }

        private sealed class StubForwarder(HttpMessageHandler handler) : IForwarderHttpClientFactory
        {
            public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
                new(handler, disposeHandler: false);
        }
    }
}
