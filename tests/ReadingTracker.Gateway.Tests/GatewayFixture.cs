using Microsoft.AspNetCore.Authentication.JwtBearer;
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

    public const string AllowedOrigin = "http://frontend.test";

    /// <summary>Stands in for Google: publishes the signing keys and mints the tokens.</summary>
    public FakeGoogle Google { get; } = new();

    private readonly StubHttpMessageHandler _googleTransport = new();

    /// <summary>A client carrying a valid token for one reader, the way the browser will.</summary>
    public HttpClient ClientFor(string subject)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Google.Token(subject));
        return client;
    }

    /// <summary>A client carrying a token this test built for itself.</summary>
    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    /// <summary>
    /// Resets the stub to answering 200 with nothing recorded yet, and returns the live list it
    /// records into — so a test can assert on what actually arrived downstream, which is the only
    /// place most of the Gateway's behaviour is visible. The fixture is shared across the
    /// collection, so every test starts by calling this.
    /// </summary>
    public IReadOnlyList<HttpRequestMessage> ResetWithOkResponses()
    {
        Downstream.Requests.Clear();
        Downstream.Respond = _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        return Downstream.Requests;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ReverseProxy:Clusters:catalog:Destinations:primary:Address", $"http://{CatalogHost}/");
        builder.UseSetting("ReverseProxy:Clusters:library:Destinations:primary:Address", $"http://{LibraryHost}/");
        builder.UseSetting("Google:ClientId", FakeGoogle.ClientId);
        builder.UseSetting("AllowedOrigins:0", AllowedOrigin);

        _googleTransport.Respond = Google.Answer;

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IForwarderHttpClientFactory>(new StubForwarder(Downstream));

            // Google is stubbed at the network boundary too: the Gateway still fetches the
            // discovery document and the signing keys for itself, and still checks signatures
            // against them. Only the socket is replaced.
            services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options => options.Backchannel = new HttpClient(_googleTransport));
        });
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
