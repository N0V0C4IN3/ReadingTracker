using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ReadingTracker.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddOidcAuthentication(options =>
{
    // Authority and client id come from wwwroot/appsettings.json. The client id is public by
    // design — it ships inside this application, where anyone can read it — and there is no
    // client secret: a browser cannot keep one.
    builder.Configuration.Bind("Google", options.ProviderOptions);

    // The authorisation code flow with PKCE. The older implicit flow would put tokens in the
    // URL, where they end up in history and server logs.
    options.ProviderOptions.ResponseType = "code";

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

await builder.Build().RunAsync();
