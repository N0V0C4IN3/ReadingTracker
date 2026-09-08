// Reaches into the authentication library's own user object for the raw ID token string.
//
// Microsoft.AspNetCore.Components.WebAssembly.Authentication only ever surfaces an
// "access_token" through its public C# API (IAccessTokenProvider.RequestAccessToken), never
// the id_token — confirmed by reading its own source. This application deliberately has no
// access_token: Google's Web application OAuth client type cannot complete the code+PKCE
// exchange that would produce one from a public client (see Program.cs), so sign-in uses the
// implicit id_token flow instead. The Gateway validates that ID token (ADR-0001), so this is
// the one token this application actually needs to send.
//
// window.AuthenticationService is the same object the framework's own JS interop calls
// through; this does not reach around authentication, it reaches around one specific
// unexported field on a class the framework already trusts.
export async function getIdToken() {
    const user = await window.AuthenticationService.getUser();
    return user?.id_token ?? null;
}
