using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using ReadingTracker.Web;
using ReadingTracker.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.Configure<DevSignInOptions>(builder.Configuration.GetSection(DevSignInOptions.SectionName));

// Read here as well as bound above: which authentication stack gets registered at all is a
// startup decision, and there is no service provider to ask yet.
var gateway = builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>()
    ?? new GatewayOptions();

var devSignIn = builder.Configuration.GetSection(DevSignInOptions.SectionName).Get<DevSignInOptions>()
    ?? new DevSignInOptions();

// The two sign-in paths are alternatives, not a pair: with DevSignIn on, Google's OIDC stack is
// never registered, which is what "temporarily disable Google auth" means here. Committed
// configuration has this off; the only file that turns it on is wwwroot/appsettings.Development.json,
// which a browser fetches only when the app is served in the Development environment. The Gateway
// gates the same switch again, independently, before it will mint a token. See ADR-0009.
if (devSignIn.Enabled)
{
    // AddOidcAuthentication would normally bring this in; nothing else does.
    builder.Services.AddAuthorizationCore();

    builder.Services.AddScoped<DevAuthenticationStateProvider>();
    builder.Services.AddScoped<AuthenticationStateProvider>(serviceProvider =>
        serviceProvider.GetRequiredService<DevAuthenticationStateProvider>());

    builder.Services.AddTransient<DevSignInAuthorizationHandler>();

    // Its own client, with no authorization handler on it: this is the call that goes and fetches
    // the token, so it cannot be a call that expects to already hold one.
    builder.Services.AddHttpClient<DevSignInSession>(client => client.BaseAddress = gateway.BaseAddress);
}
else
{
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

        // Google puts the display name in "name"; without this the reader would be greeted by
        // their subject id, which is a number nobody recognises.
        options.UserOptions.NameClaim = "name";
    });

    builder.Services.AddScoped<IdTokenProvider>();
    builder.Services.AddTransient<GatewayAuthorizationHandler>();
}

// Every client this application makes points at the Gateway, the only address this
// application is allowed to know — never at Catalog or Library directly.
AddGatewayClient<GatewayLibraryClient>();
AddGatewayClient<GatewayCatalogClient>();

var host = builder.Build();

if (!devSignIn.Enabled)
{
    // Installed before anything renders, so it is already watching when the callback page runs
    // its own sign-in completion. See wwwroot/js/signInDiagnostics.js for why a failed sign-in
    // otherwise leaves no trace at all.
    //
    // Nothing here is allowed to stop the application starting. This only ever explains a
    // failure; an application that refuses to boot because the thing that explains failures
    // could not be loaded has turned a diagnostic into an outage — which is exactly what
    // happened the first time this was written without the catch, when a stale asset hash
    // blocked the import and took the whole page down with it.
    try
    {
        var module = await host.Services.GetRequiredService<IJSRuntime>()
            .InvokeAsync<IJSObjectReference>("import", "./js/signInDiagnostics.js");

        await module.InvokeVoidAsync("watchSignInCallback");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Sign-in diagnostics could not be installed: {exception.Message}");
    }
}

await host.RunAsync();

void AddGatewayClient<TClient>()
    where TClient : class
{
    var client = builder.Services.AddHttpClient<TClient>(http => http.BaseAddress = gateway.BaseAddress);

    // Whichever token this build signs in with, it is attached the same way and to every request.
    if (devSignIn.Enabled)
    {
        client.AddHttpMessageHandler<DevSignInAuthorizationHandler>();
    }
    else
    {
        client.AddHttpMessageHandler<GatewayAuthorizationHandler>();
    }
}
