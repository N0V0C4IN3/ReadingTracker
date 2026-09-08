using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// Proves the second gate independently of the first: DevSignIn:Enabled=true alone is not
/// enough. Everything here matches <see cref="DevSignInFixture"/> except the hosting
/// environment, which is the one thing a deployed environment's configuration cannot fake
/// (ADR-0008) — so this is the test that actually backs that claim, rather than just the code
/// reading it correctly.
/// </summary>
public sealed class DevSignInStaysOffOutsideDevelopmentTests
{
    [Fact]
    public async Task Configuration_alone_does_not_enable_it_outside_development()
    {
        using var factory = new ProductionWithDevSignInConfiguredFixture();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/dev/sign-in", JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class ProductionWithDevSignInConfiguredFixture : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ReverseProxy:Clusters:catalog:Destinations:primary:Address", "http://catalog.test/");
            builder.UseSetting("ReverseProxy:Clusters:library:Destinations:primary:Address", "http://library.test/");
            builder.UseSetting("Google:ClientId", FakeGoogle.ClientId);
            builder.UseSetting("AllowedOrigins:0", "http://frontend.test");
            builder.UseSetting("DevSignIn:Enabled", "true");

            builder.ConfigureTestServices(services =>
                services.AddSingleton<IForwarderHttpClientFactory>(new StubForwarder(new StubHttpMessageHandler())));
        }

        private sealed class StubForwarder(HttpMessageHandler handler) : IForwarderHttpClientFactory
        {
            public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
                new(handler, disposeHandler: false);
        }
    }
}
