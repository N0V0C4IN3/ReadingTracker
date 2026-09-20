using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// The Gateway now owns a table (ADR-0014), and a Gateway with nowhere to keep it should say so
/// at startup, the way it already does for a missing Google client id or allowed origin —
/// rather than come up, pass its health check, and fail on the first device that calls.
/// </summary>
public sealed class GatewayRefusesToStartTests
{
    [Fact]
    public void Without_a_connection_string_for_its_own_schema()
    {
        using var factory = new WithoutConnectionStringFixture();

        var refusal = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings__GatewayDb", refusal.Message);
    }

    private sealed class WithoutConnectionStringFixture : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // A deployment, not a developer's machine: appsettings.Development.json points at the
            // compose Postgres, and it is the deployed Gateway that could be left without one.
            builder.UseEnvironment("Production");
            builder.UseSetting("ReverseProxy:Clusters:catalog:Destinations:primary:Address", "http://catalog.test/");
            builder.UseSetting("ReverseProxy:Clusters:library:Destinations:primary:Address", "http://library.test/");
            builder.UseSetting("Google:ClientId", FakeGoogle.ClientId);
            builder.UseSetting("AllowedOrigins:0", "http://frontend.test");
        }
    }
}
