# ReadingTracker

A web application for tracking personal reading progress across books — what you're reading, how far you've gotten, and your history of reading activity.

Built as a portfolio project, with the weight on the back end: several small services, real integration tests against real infrastructure, and architectural decisions written down rather than implied.

## Status

**Catalog**, **Library**, the **Gateway** and a first cut of **Web** are built: a reader signs in with Google and sees their own shelf, end to end.

| Service | What it does | Status |
| --- | --- | --- |
| Catalog | Finds books via Google Books and Open Library, caches them, gives them stable ids | Built |
| Library | A reader's own books: reading status, progress, and reading history | Built |
| Gateway | Single entry point; verifies the caller's Google token and names the reader | Built |
| Web | Blazor WebAssembly front end: sign in, see your shelf | Reads only |
| Identity | Maps a Google account to an internal reader | Designed |

> **Everything through the Gateway now needs a verified token.** It verifies the token against Google's published keys, discards whatever `X-Reader-Id` the caller sent, and sets that header itself from the token's subject. `/health` is the only route that answers without one. Locally you sign in without a Google account at all — see [Signing in locally](#signing-in-locally-without-a-google-account).

## Running it locally

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) and Docker.

```bash
# 1. Start Postgres and RabbitMQ
docker compose up -d

# 2. Configure the Google Books API key (see below)
cp .env.example .env   # then fill in GOOGLE_BOOKS_API_KEY

# 3. Run the services, each in its own terminal
dotnet run --project src/ReadingTracker.Catalog   # http://localhost:5103
dotnet run --project src/ReadingTracker.Library   # http://localhost:5110
dotnet run --project src/ReadingTracker.Gateway   # http://localhost:5100
dotnet run --project src/ReadingTracker.Web       # http://localhost:5200
```

> **Port 5200 is not arbitrary.** It is the JavaScript origin registered with this project's Google OAuth client, and sign-in fails from anywhere else. The same goes for the redirect path `/authentication/login-callback`.

**Or, all four services in one command:**

```bash
docker compose --profile services up --build
```

This still starts Postgres and RabbitMQ — `--profile services` adds the four .NET services on top, each in its own container, each still on the ports above. `--build` picks up code changes; drop it once the images already reflect what you're running. Plain `docker compose up` (no profile) stays exactly what it has always been — Postgres and RabbitMQ only — because container rebuilds are slower than `dotnet run`'s edit/rebuild loop, and the four containers would just be in the way while actively changing one service's code.

### Signing in locally, without a Google account

Run either way above and Web greets you with a **reader id box and a "Sign in (dev)" button**, not
Google. Type any reader id — `ada`, `dev-reader`, anything — and you are that reader: your own
shelf, your own entries, invisible to every other reader. Sign out and back in as someone else to
watch that hold. Nobody needs a Google account, and nothing touches a real one.

This is on because both halves opt in, and both are local-only. The Gateway mints these tokens only
when it is in the Development environment *and* `DevSignIn:Enabled` is set (ADR-0008) — `dotnet run`
and `docker compose` each set both. Web swaps Google's sign-in out for it only when served in the
Development environment, via `wwwroot/appsettings.Development.json` (ADR-0009). Neither condition
can hold in a deployed environment, and neither works without the other.

Two things follow from tokens being minted per Gateway process:

- **Restarting the Gateway invalidates the token your browser is holding** — its signing key is
  generated fresh every start and never written down. Web notices the `401`, mints a replacement
  and retries, so in practice a restart is invisible. You do not need to sign in again.
- **To drive the API by hand**, get a token the same way Web does:

  ```bash
  TOKEN=$(curl -s -X POST http://localhost:5100/dev/sign-in \
    -H 'Content-Type: application/json' -d '{"readerId":"ada"}' | jq -r .idToken)

  curl http://localhost:5100/api/library -H "Authorization: Bearer $TOKEN"
  ```

**To exercise the real Google sign-in instead**, set `DevSignIn.Enabled` to `false` in
`src/ReadingTracker.Web/wwwroot/appsettings.Development.json`. The two are alternatives — with dev
sign-in on, Google's OIDC stack is not registered at all, which is the point of it being a switch.

The OAuth client is in Google's *Testing* status, so only accounts added as test users can sign in, and Google shows an "unverified app" warning first. Both are expected. Publishing needs a public home page, privacy policy and terms of service on a verified domain, which is deployment-time work.

With the Gateway up, one address reaches everything: `/api/books/…` goes to Catalog and `/api/library/…` to Library — but only with a token it has verified, so `curl` alone gets you a `401`. Sign in through Web, or mint a dev token as above, to see it work.

Web talks to the Gateway across origins, so the Gateway only answers requests from Web's own origin — `AllowedOrigins` in its configuration — rather than from anywhere. Web itself never talks to Catalog or Library directly; it only knows the Gateway's address.

Web sends the **ID token** Google issued at sign-in, not an access token: Google's *Web application* OAuth client type cannot complete the authorisation-code exchange from a public client such as a browser (there is no client secret to send, and Google — unlike most OIDC providers — has no PKCE-only exception for this client type), so sign-in uses the implicit `id_token` flow instead. That token is also exactly what the Gateway needs, since ADR-0001 has it validating ID tokens.

The two services can still be called directly on their own ports, which is how you drive them by hand locally. That is exactly what must be impossible once deployed: anything that can reach them directly can claim to be any reader by setting a header. See ADR-0007.

Each service exposes `/health`. For Catalog and Library it returns `Healthy` only when they can actually reach their own database; the Gateway owns no database, so its health check says only that the Gateway itself is up. Catalog and Library share one Postgres instance but own separate schemas and never read each other's tables.

> **Ports.** Postgres is on **55432** and RabbitMQ on **55672** (management UI on **55673**), deliberately off the default ports so they don't collide with anything already installed locally.

### The Google Books API key

You need one. Google quotas *keyless* access to the Books API at **zero** requests per day, so book search cannot reach the live API without a key.

1. Create a project at [console.cloud.google.com](https://console.cloud.google.com/).
2. Enable the [Books API](https://console.cloud.google.com/apis/library/books.googleapis.com).
3. **APIs & Services → Credentials → Create credentials → API key**.
4. Put it in `.env` as `GOOGLE_BOOKS_API_KEY`.

`.env` is gitignored. Deployed environments supply configuration directly rather than through a file. Open Library needs no key and is used as a fallback.

## The Catalog API

| Endpoint | Purpose |
| --- | --- |
| `GET /health` | Healthy only when the database is reachable |
| `GET /api/books/search?isbn=` | Find a specific edition |
| `GET /api/books/search?title=&author=` | Find candidates; author narrows the search |
| `GET /api/books/{bookId}` | Read a stored book |
| `POST /api/books` | Add a book by hand when no provider has it |

Search consults Google Books first, then Open Library. Three outcomes are deliberately distinct:

- **`200` with results** — found.
- **`200` with an empty list** — the providers answered, and nobody has this book.
- **`503`** — no provider could be reached, so we genuinely don't know. Reporting "no results" here would tell you a book doesn't exist when we simply couldn't ask.

Books are cached the first time they're referenced, so repeat lookups don't hit a provider again, and every book gets a stable id that other services can point at. Storing a new book publishes a `BookCached` event to RabbitMQ.

## Tests

```bash
dotnet test
```

Integration tests boot the real application in-process against a throwaway Postgres and RabbitMQ (via Testcontainers), and drive it over HTTP rather than reaching into internals. External book providers are stubbed at the network boundary, so the real provider code — including how it parses each provider's JSON — runs under test while no test ever touches the live APIs.

Docker must be running.

## Design documentation

- [`CONTEXT.md`](./CONTEXT.md) — the domain glossary. Worth reading first: it defines `Book`, `LibraryEntry`, `ReadingSession`, `ReadingStatus` and `TrackingMethod`, and is deliberately free of implementation detail.
- [`docs/adr/`](./docs/adr/) — architectural decisions and, more usefully, why the alternatives were rejected.
