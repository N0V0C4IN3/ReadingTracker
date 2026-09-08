using System.Net.Http.Headers;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// The browser calls the Gateway across origins, so it needs to say which origins it will
/// answer — but a wildcard would let any site on the internet ride a signed-in reader's browser
/// into making requests here. Only the configured origin is allowed.
/// </summary>
[Collection(GatewayCollection.Name)]
public sealed class CorsTests(GatewayFixture fixture)
{
    [Fact]
    public async Task Lets_the_configured_frontend_call_it()
    {
        fixture.ResetWithOkResponses();
        var client = fixture.ClientFor("cors-reader");
        client.DefaultRequestHeaders.Add("Origin", GatewayFixture.AllowedOrigin);

        var response = await client.GetAsync("/api/library");

        Assert.Contains(
            response.Headers,
            header => header.Key == "Access-Control-Allow-Origin"
                && header.Value.Contains(GatewayFixture.AllowedOrigin));
    }

    [Fact]
    public async Task Does_not_let_an_arbitrary_site_call_it()
    {
        fixture.ResetWithOkResponses();
        var client = fixture.ClientFor("cors-stranger");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.test");

        var response = await client.GetAsync("/api/library");

        Assert.DoesNotContain(response.Headers, header => header.Key == "Access-Control-Allow-Origin");
    }

    [Fact]
    public async Task Approves_the_browsers_preflight_for_the_configured_origin()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/library");
        request.Headers.Add("Origin", GatewayFixture.AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization");

        var response = await fixture.CreateClient().SendAsync(request);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains(
            response.Headers,
            header => header.Key == "Access-Control-Allow-Origin"
                && header.Value.Contains(GatewayFixture.AllowedOrigin));
    }
}
