// Reaches into the authentication library's own user object for the raw ID token string.
//
// Microsoft.AspNetCore.Components.WebAssembly.Authentication only ever surfaces an
// "access_token" through its public C# API (IAccessTokenProvider.RequestAccessToken), never
// the id_token. This application deliberately has no access_token: Google's Web application
// OAuth client type cannot complete the code+PKCE exchange that would produce one from a public
// client (see Program.cs), so sign-in uses the implicit id_token flow instead. The Gateway
// validates that ID token (ADR-0001), so this is the one token this application needs to send.
//
// Note which object this asks. window.AuthenticationService.getUser() is NOT it: that returns
// `user.profile` — the decoded claims (sub, name, email, iss, aud) and nothing else. There is no
// id_token on the profile, so asking it for one silently yields undefined, every request goes out
// unauthenticated, and the Gateway's 401 shows up as "your session has ended" forever. The raw
// token lives on the user object itself, one level up, which is what _userManager.getUser()
// returns — the same call AuthenticationService makes before it narrows to the profile.
export async function getIdToken() {
    const userManager = window.AuthenticationService?.instance?._userManager;

    if (!userManager) {
        // The framework's internals moved. Say so loudly: the failure this produces otherwise is
        // a permanent, silent 401 that looks like an expired session rather than a broken build.
        console.error(
            "Could not reach the authentication library's user manager, so no ID token can be " +
            "sent and every Gateway call will be refused. See wwwroot/js/idToken.js.");

        return null;
    }

    const user = await userManager.getUser();

    // No user at all is ordinary: nobody is signed in.
    if (!user) {
        return null;
    }

    if (!user.id_token) {
        console.error(
            "A reader is signed in but their user object carries no id_token, so the Gateway " +
            "will refuse every call. See wwwroot/js/idToken.js.");

        return null;
    }

    return user.id_token;
}
