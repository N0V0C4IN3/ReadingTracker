using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Gateway.Tests;

[Collection(GatewayCollection.Name)]
public sealed class ForwardingTests(GatewayFixture fixture)
{
    [Fact]
    public async Task Sends_book_requests_to_the_catalog()
    {
        var seen = fixture.ResetWithOkResponses();

        var response = await fixture.CreateClient().GetAsync("/api/books/search?isbn=9780441013593");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GatewayFixture.CatalogHost, seen.Single().RequestUri!.Host);
    }

    [Fact]
    public async Task Sends_library_requests_to_the_library()
    {
        var seen = fixture.ResetWithOkResponses();

        var response = await fixture.CreateClient().GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GatewayFixture.LibraryHost, seen.Single().RequestUri!.Host);
    }

    [Fact]
    public async Task Carries_the_path_and_query_string_through_untouched()
    {
        var seen = fixture.ResetWithOkResponses();

        await fixture.CreateClient().GetAsync("/api/books/search?title=Dune&author=Herbert");

        var forwarded = seen.Single().RequestUri!;
        Assert.Equal("/api/books/search", forwarded.AbsolutePath);
        Assert.Equal("?title=Dune&author=Herbert", forwarded.Query);
    }

    [Fact]
    public async Task Carries_the_method_and_body_through_untouched()
    {
        fixture.ResetWithOkResponses();
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
    public async Task Carries_my_headers_through_to_the_service()
    {
        var seen = fixture.ResetWithOkResponses();
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Reader-Id", "header-reader");
        client.DefaultRequestHeaders.Add("X-Invented-Later", "still arrives");

        await client.GetAsync("/api/library");

        var forwarded = seen.Single();
        Assert.Equal("still arrives", forwarded.Headers.GetValues("X-Invented-Later").Single());

        // The reader header passes straight through today, which is exactly what makes the
        // Gateway open until token verification lands. This assertion is meant to be inverted
        // then, not deleted: ADR-0007 requires the Gateway to strip it.
        Assert.Equal("header-reader", forwarded.Headers.GetValues("X-Reader-Id").Single());
    }

    [Fact]
    public async Task Tells_the_service_who_the_request_really_came_from()
    {
        var seen = fixture.ResetWithOkResponses();

        await fixture.CreateClient().GetAsync("/api/library");

        // A proxy is not a pipe. The caller's headers survive, but the hop itself is declared,
        // because the service would otherwise believe the Gateway was the original caller.
        Assert.Contains(
            seen.Single().Headers,
            header => header.Key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reaches_endpoints_that_did_not_exist_when_the_gateway_was_written()
    {
        var seen = fixture.ResetWithOkResponses();

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

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("could not be reached", body);
        Assert.Contains("library", body);
    }

    [Fact]
    public async Task Tells_a_service_that_is_down_apart_from_one_that_answered_502()
    {
        fixture.Downstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);
        var answered = await fixture.CreateClient().GetAsync("/api/library");

        fixture.Downstream.Respond = _ => throw new HttpRequestException("connection refused");
        var unreachable = await fixture.CreateClient().GetAsync("/api/library");

        // Both arrive as 502, so the status alone cannot tell them apart — "the service is down"
        // and "the service said no" are different facts, and the body is what separates them.
        Assert.Equal(HttpStatusCode.BadGateway, answered.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, unreachable.StatusCode);
        Assert.DoesNotContain("could not be reached", await answered.Content.ReadAsStringAsync());
        Assert.Contains("could not be reached", await unreachable.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Answers_a_health_check_without_bothering_anything_downstream()
    {
        var seen = fixture.ResetWithOkResponses();

        var response = await fixture.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Does_not_forward_a_path_that_belongs_to_no_service()
    {
        var seen = fixture.ResetWithOkResponses();

        var response = await fixture.CreateClient().GetAsync("/api/nonsense");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(seen);
    }
}
