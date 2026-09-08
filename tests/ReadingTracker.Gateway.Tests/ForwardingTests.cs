using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Gateway.Tests;

[Collection(GatewayCollection.Name)]
public sealed class ForwardingTests(GatewayFixture fixture)
{
    [Fact]
    public async Task Sends_book_requests_to_the_catalog()
    {
        var seen = fixture.RespondWithOk();

        var response = await fixture.CreateClient().GetAsync("/api/books/search?isbn=9780441013593");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GatewayFixture.CatalogHost, seen.Single().RequestUri!.Host);
    }

    [Fact]
    public async Task Sends_library_requests_to_the_library()
    {
        var seen = fixture.RespondWithOk();

        var response = await fixture.CreateClient().GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GatewayFixture.LibraryHost, seen.Single().RequestUri!.Host);
    }

    [Fact]
    public async Task Carries_the_path_and_query_string_through_untouched()
    {
        var seen = fixture.RespondWithOk();

        await fixture.CreateClient().GetAsync("/api/books/search?title=Dune&author=Herbert");

        var forwarded = seen.Single().RequestUri!;
        Assert.Equal("/api/books/search", forwarded.AbsolutePath);
        Assert.Equal("?title=Dune&author=Herbert", forwarded.Query);
    }

    [Fact]
    public async Task Carries_the_method_and_body_through_untouched()
    {
        fixture.RespondWithOk();
        var bookId = Guid.NewGuid();
        HttpMethod? method = null;
        string? body = null;

        // Read while the call is in flight: a proxied body is streamed from the live request,
        // so by the time the caller has its response there is nothing left to read.
        fixture.Downstream.RespondAsync = async (request, cancellationToken) =>
        {
            method = request.Method;
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        await fixture.CreateClient().PostAsJsonAsync("/api/library", new { bookId });

        Assert.Equal(HttpMethod.Post, method);
        Assert.Contains(bookId.ToString(), body);
    }

    [Fact]
    public async Task Reaches_endpoints_that_did_not_exist_when_the_gateway_was_written()
    {
        var seen = fixture.RespondWithOk();

        // Nothing about this path is known to the Gateway; adding an endpoint downstream must
        // never mean editing the Gateway.
        await fixture.CreateClient().PutAsync("/api/library/anything/invented-later", null);

        Assert.Equal("/api/library/anything/invented-later", seen.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Passes_a_downstream_answer_back_as_itself()
    {
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable, HttpStatusCode.Conflict })
        {
            fixture.Downstream.Respond = _ => new HttpResponseMessage(status);

            var response = await fixture.CreateClient().GetAsync("/api/library");

            // A downstream 404 is a 404, not a Gateway error dressed up as one.
            Assert.Equal(status, response.StatusCode);
        }
    }

    [Fact]
    public async Task Says_something_different_when_a_service_cannot_be_reached_at_all()
    {
        fixture.Downstream.Respond = _ => throw new HttpRequestException("connection refused");

        var response = await fixture.CreateClient().GetAsync("/api/library");

        // "The service is down" and "the service said no" are different facts, and an operator
        // reading the logs needs to be able to tell them apart.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Answers_a_health_check_without_bothering_anything_downstream()
    {
        var seen = fixture.RespondWithOk();

        var response = await fixture.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Does_not_forward_a_path_that_belongs_to_no_service()
    {
        var seen = fixture.RespondWithOk();

        var response = await fixture.CreateClient().GetAsync("/api/nonsense");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(seen);
    }
}
