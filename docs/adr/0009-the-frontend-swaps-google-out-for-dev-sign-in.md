# The frontend swaps Google out for dev sign-in, rather than offering both

ADR-0008 gave the Gateway a dev-only endpoint that mints a reader identity without a real Google
account, but nothing in the browser application could use it: `AddOidcAuthentication` is wired
straight to Google, and the ID token is read out of the OIDC library's own session. A developer —
or an agent, which must never touch a real reader's Google credentials — could get a token with
`curl` and nothing more. Every path through the actual UI still required signing in with Google,
so the shelf and book search could not be exercised at all.

## Decision

When `DevSignIn:Enabled` is true in the browser application's configuration, `Program` registers a
`DevAuthenticationStateProvider` and **does not register Google's OIDC stack at all**. The two are
alternatives, not a pair: this is what "temporarily disable Google auth" means here. In that mode

- `DevSignInSession` calls the Gateway's `POST /dev/sign-in` for a caller-chosen reader id and
  keeps the token in `localStorage`, so a reload does not mean signing in again.
- `DevSignInAuthorizationHandler` attaches that token in place of `GatewayAuthorizationHandler`,
  and on a 401 mints a replacement once and retries.
- `/authentication/{action}` renders the dev sign-in form instead of `RemoteAuthenticatorView`,
  whose services are not registered in this mode.

Two independent gates, mirroring ADR-0008's. The committed `wwwroot/appsettings.json` has this
off; the only file that turns it on is `wwwroot/appsettings.Development.json`, which a browser
fetches only when the app is served in the Development environment. And a token still has to come
from a Gateway that independently passes ADR-0008's own two gates, so flipping this on alone gets
nobody signed in to anything.

## Consequences

The security properties are unchanged: this mints no tokens of its own and weakens no validation.
The Gateway still verifies whatever token arrives and still sets `X-Reader-Id` only from a claim it
verified itself, so ADR-0007 holds and Catalog and Library cannot tell a dev reader from a real
one. CONTEXT.md's rule that a Reader is "identified by whoever signed in, never by anything the
client claims about itself" also holds — the browser decodes the token's `sub` only to put a name
on screen, and the Gateway never trusts that.

Google sign-in cannot be exercised locally while this is on, since its stack is not registered.
Setting `Enabled` to false in `wwwroot/appsettings.Development.json` puts it back. That is the
price of the switch being a real switch rather than two half-live paths, and it is worth paying:
the alternative left the UI untestable without a personal Google account.

Two consequences worth naming because neither is obvious and both caused real bugs:

**Signing in no longer necessarily reloads the page.** Google's sign-in is a redirect out and back,
so a component could fetch in `OnInitializedAsync` and be sure it ran with a reader present. Dev
sign-in happens in place. `Home` therefore reacts to the cascaded authentication state rather than
to initialisation, and clears one reader's results before loading the next one's.

**`DevSignInSession` must read the token from storage on every request, never from a field.**
`IHttpClientFactory` builds a handler chain once and reuses it for minutes, so the session captured
inside the authorization handler outlives any single sign-in. Cached in memory, signing in as a
second reader kept sending the first reader's token — which showed one reader another reader's
shelf, while looking nothing like an authentication bug from the UI.

## Considered Options

**Decorating the OIDC `AuthenticationStateProvider` so both paths stay live** was considered and
rejected. `AddOidcAuthentication` registers `IRemoteAuthenticationService` and `IAccessTokenProvider`
as casts of the same `AuthenticationStateProvider` registration, so wrapping it means re-registering
all three against a concrete framework-internal type and depending on its constructor shape. That is
a lot of coupling to framework internals, and it buys a mode nobody asked for — being half signed in
to Google and half not.

**Reusing `GatewayAuthorizationHandler` with a branch inside it** was rejected so the Google path
keeps no dev-only code on it. The retry-on-401 behaviour makes sense only for a token this
application can mint again, which is exactly what a Google token is not.

**Persisting nothing, and minting a token on every page load** was rejected: it would make signing
out impossible to represent, and quietly re-sign-in a developer who meant to be signed out.
