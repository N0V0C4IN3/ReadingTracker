using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// A Gateway with DevSignIn turned on, the way a developer running locally would. Separate from
/// <see cref="GatewayFixture"/> because most tests must prove DevSignIn is off by default — this
/// fixture exists only to prove the enabled path works when a developer actually opts in.
/// </summary>
public sealed class DevSignInFixture : WebApplicationFactory<Program>
{
    public const string CatalogHost = "catalog.test";

    public const string LibraryHost = "library.test";

    public const string AllowedOrigin = "http://frontend.test";

    public const string ClientId = FakeGoogle.ClientId;

    public StubHttpMessageHandler Downstream { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ReverseProxy:Clusters:catalog:Destinations:primary:Address", $"http://{CatalogHost}/");
        builder.UseSetting("ReverseProxy:Clusters:library:Destinations:primary:Address", $"http://{LibraryHost}/");
        builder.UseSetting("Google:ClientId", ClientId);
        builder.UseSetting("AllowedOrigins:0", AllowedOrigin);
        builder.UseSetting("DevSignIn:Enabled", "true");

        builder.ConfigureTestServices(services =>
            services.AddSingleton<IForwarderHttpClientFactory>(new StubForwarder(Downstream)));
    }

    private sealed class StubForwarder(HttpMessageHandler handler) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
            new(handler, disposeHandler: false);
    }
}

[CollectionDefinition(Name)]
public sealed class DevSignInCollection : ICollectionFixture<DevSignInFixture>
{
    public const string Name = "gateway-dev-sign-in";
}
