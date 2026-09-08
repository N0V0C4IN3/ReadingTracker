# A dev-only sign-in that bypasses Google, gated behind two independent checks

Local development and automated testing sometimes need a valid, Gateway-verified reader identity without a real Google account — and, as a standing rule, an agent working on this codebase must never touch a real reader's Google credentials. The Gateway can optionally mint its own tokens for exactly this.

## Decision

When `DevSignIn:Enabled` is `true` in configuration **and** the host is actually running in the ASP.NET Core `Development` environment, the Gateway:

- Generates a symmetric signing key at process startup, held only in memory — never a file, never committed, never reused across a restart.
- Accepts tokens issued under `reading-tracker-dev` and signed with that key, alongside real Google-issued tokens, through the same JwtBearer validation Google tokens already go through (issuer, audience, lifetime, and signature are all still checked).
- Exposes `POST /dev/sign-in`, anonymous, minting a token for a caller-supplied reader id.

Both gates are independent. Configuration alone cannot enable this in a deployed environment, since nothing there runs the Development environment (ADR-0005's Azure Container Apps target sets `ASPNETCORE_ENVIRONMENT=Production`). The Development environment alone does not enable it either, since nothing defaults `DevSignIn:Enabled` to true — a developer opts in explicitly (docker-compose's local-only service definition does this for local runs).

## Consequences

Real Google-token validation is unmodified — this only adds a second, independently-gated acceptance path, and never weakens the first. A reader identity obtained this way is otherwise indistinguishable to Catalog or Library from a real one: the Gateway still verifies whichever token arrived and still sets `X-Reader-Id` only from a claim it verified itself, so ADR-0007 holds unchanged and nothing downstream needs to special-case it.

## Considered Options

**A second, separate authentication scheme selected by policy** was considered and rejected as more moving parts for the same outcome. Merging the dev issuer and key into the existing scheme's `TokenValidationParameters` produces identical security properties with substantially less code, because the ASP.NET Core JwtBearer handler already concatenates whatever issuers and keys `Authority` discovers onto whatever is configured statically, at validation time — adding a second static issuer and key is enough.

**A committed, static dev signing key** was considered and rejected: a key that exists only in memory and is regenerated every process start means there is never a secret to leak, rotate, or accidentally reuse against a real deployment.
