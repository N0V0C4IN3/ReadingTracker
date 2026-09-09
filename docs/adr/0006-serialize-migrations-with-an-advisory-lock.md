# Services migrate their own schema on boot, serialised by a Postgres advisory lock

Each service applies its EF Core migrations at startup rather than as a separate deployment step, but takes a fixed Postgres advisory lock first so that concurrent replicas serialise instead of racing on the same schema. ADR-0005 puts services on Azure Container Apps, which scales to zero and can cold-start several replicas simultaneously, and EF Core's `Migrate()` is not safe to run concurrently.

## Considered Options

**A separate migration step** (a one-off job in the deploy pipeline, with services never migrating) is what a production system would more likely do: it decouples schema changes from application startup and makes rollbacks legible. It was rejected *for now* only because no deployment pipeline exists yet — deployment is explicitly out of scope for the Catalog spec — and adopting it would mean the test seam has to apply migrations itself. This is the natural thing to revisit when deployment work starts.

**Leaving the race in place** was rejected: it is a real defect under the chosen hosting model, not a theoretical one.

## Consequences

- An unreachable database still kills the process at startup, so `/health` cannot report the database unhealthy at boot — the health check's failure path only covers a database that dies after a successful start. Accepted deliberately: a service that cannot reach its own database has nothing useful to serve.
- Each service must choose its own distinct lock key; reusing another service's key would serialise unrelated deployments.
- **The choice of a session lock constrains how services may connect.** `pg_advisory_lock` is held by a session, which is why the migration code opens one connection and keeps the lock and the migration on it. A connection pooler in transaction pooling mode — PgBouncer, and so Neon's `-pooler` endpoint — only ties a client to a backend for the length of a transaction, so the lock, the migration and the unlock can each land on a different backend. Nothing errors: the lock simply is not held while the migration runs, which is the race this ADR exists to prevent, and the unlock silently fails leaving the lock stranded. Services must use a direct, non-pooled endpoint. A transaction-scoped `pg_advisory_xact_lock` would be pooler-safe, but EF Core's `Migrate()` manages its own transactions, so the lock cannot be made to share one with it.
