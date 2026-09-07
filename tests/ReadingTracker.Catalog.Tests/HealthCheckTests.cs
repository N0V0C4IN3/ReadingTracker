using System.Net;

namespace ReadingTracker.Catalog.Tests;

[Collection(CatalogApiCollection.Name)]
public sealed class HealthCheckTests(CatalogApiFixture fixture)
{
    [Fact]
    public async Task Reports_healthy_when_the_service_can_reach_its_own_database()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
