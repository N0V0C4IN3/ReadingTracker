using System.Net;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class HealthCheckTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Reports_healthy_when_the_service_can_reach_its_own_database()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Stays_healthy_when_catalog_cannot_be_reached()
    {
        // Library's own health does not depend on Catalog: a Catalog outage should degrade
        // book details, never take this service down.
        fixture.Catalog.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var response = await fixture.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
