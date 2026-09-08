using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace ReadingTracker.Gateway.Tests;

/// <summary>
/// Boots the real Gateway in-process. This is the single seam the suite exercises: tests drive
/// the Gateway through its own HTTP surface and observe what reached the services behind it.
///
/// Unlike Catalog's and Library's fixtures there are no containers to start, because the Gateway
/// owns no database and no queue. Catalog and Library are stubbed at the network boundary rather
/// than by replacing anything inside the Gateway, so the genuine forwarding path runs.
/// </summary>
public sealed class GatewayFixture : WebApplicationFactory<Program>
{
    /// <summary>
    /// Stands in for both services behind the Gateway. They are told apart by the host the
    /// Gateway dialled, which is what proves a request went to the right one.
    /// </summary>
    public StubHttpMessageHandler Downstream { get; } = new();

    public const string CatalogHost = "catalog.test";

    public const string LibraryHost = "library.test";

    /// <summary>
    /// Starts a test with both services answering 200 and nothing recorded yet. Returns the list
    /// the stub records into, so a test can assert on what actually arrived downstream — which is
    /// the only place most of the Gateway's behaviour is visible.
    /// </summary>
    public IReadOnlyList<HttpRequestMessage> RespondWithOk()
    {
        Downstream.Requests.Clear();
        Downstream.RespondAsync = null;
        Downstream.Respond = _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        return Downstream.Requests;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ReverseProxy:Clusters:catalog:Destinations:primary:Address", $"http://{CatalogHost}/");
        builder.UseSetting("ReverseProxy:Clusters:library:Destinations:primary:Address", $"http://{LibraryHost}/");

        builder.ConfigureTestServices(services =>
            services.AddSingleton<IForwarderHttpClientFactory>(new StubForwarder(Downstream)));
    }

    /// <summary>Hands YARP a client that talks to the stub instead of to a socket.</summary>
    private sealed class StubForwarder(HttpMessageHandler handler) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
            new(handler, disposeHandler: false);
    }
}

[CollectionDefinition(Name)]
public sealed class GatewayCollection : ICollectionFixture<GatewayFixture>
{
    public const string Name = "gateway-api";
}
