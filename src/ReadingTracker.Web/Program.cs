using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Options;
using ReadingTracker.Web;
using ReadingTracker.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddOidcAuthentication(options =>
{
    // Authority and client id come from wwwroot/appsettings.json. The client id is public by
    // design — it ships inside this application, where anyone can read it — and there is no
    // client secret: a browser cannot keep one.
    builder.Configuration.Bind("Google", options.ProviderOptions);

    // The implicit id_token flow, not authorisation code + PKCE. Google's "Web application"
    // client type requires a client secret to exchange a code for tokens even when PKCE is
    // used — there is no public-client exception, unlike most other OIDC providers — and a
    // browser cannot hold one. The code flow fails at the token exchange with a bad request;
    // this is not a configuration mistake to fix, it is a Google-specific constraint.
    //
    // This also happens to be the right shape for what comes next: the Gateway validates the
    // ID token (ADR-0001), and "id_token" hands the app exactly that, rather than the access
    // token Blazor's default flow would otherwise surface.
    options.ProviderOptions.ResponseType = "id_token";

    // Exactly the three scopes the OAuth client is registered for, and no more. These are
    // non-sensitive, which is what keeps the app out of Google's verification review.
    options.ProviderOptions.DefaultScopes.Clear();
    options.ProviderOptions.DefaultScopes.Add("openid");
    options.ProviderOptions.DefaultScopes.Add("profile");
    options.ProviderOptions.DefaultScopes.Add("email");

    // Google puts the display name in "name"; without this the reader would be greeted by their
    // subject id, which is a number nobody recognises.
    options.UserOptions.NameClaim = "name";
});

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddScoped<IdTokenProvider>();
builder.Services.AddTransient<GatewayAuthorizationHandler>();

// The only client this application makes: everything goes through the Gateway, which is the
// only address this application is allowed to know.
builder.Services.AddHttpClient<GatewayLibraryClient>((serviceProvider, client) =>
        client.BaseAddress = serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value.BaseAddress)
    .AddHttpMessageHandler<GatewayAuthorizationHandler>();

await builder.Build().RunAsync();
