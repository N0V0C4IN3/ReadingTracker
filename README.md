# ReadingTracker

A web application for tracking personal reading progress across books — what you're reading, how far you've gotten, and your history of reading activity.

Built as a portfolio project, with the weight on the back end: several small services, real integration tests against real infrastructure, and architectural decisions written down rather than implied.

## Status

The **Catalog** service is complete. The rest is designed but not yet built.

| Service | What it does | Status |
| --- | --- | --- |
| Catalog | Finds books via Google Books and Open Library, caches them, gives them stable ids | Built |
| Library | A reader's own books: reading status, progress, and reading history | In progress |
| Identity | Maps a Google account to an internal user | Designed |
| Gateway | Single entry point; validates the caller's Google token | Designed |
| Web | Blazor WebAssembly front end | Designed |

## Running it locally

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) and Docker.

```bash
# 1. Start Postgres and RabbitMQ
docker compose up -d

# 2. Configure the Google Books API key (see below)
cp .env.example .env   # then fill in GOOGLE_BOOKS_API_KEY

# 3. Run a service
dotnet run --project src/ReadingTracker.Catalog   # http://localhost:5103
dotnet run --project src/ReadingTracker.Library   # http://localhost:5110
```

Each service exposes `/health`, which returns `Healthy` only when it can actually reach its own database. Catalog and Library share one Postgres instance but own separate schemas and never read each other's tables.

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
