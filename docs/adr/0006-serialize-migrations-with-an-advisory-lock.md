# Services migrate their own schema on boot, serialised by a Postgres advisory lock

Each service applies its EF Core migrations at startup rather than as a separate deployment step, but takes a fixed Postgres advisory lock first so that concurrent replicas serialise instead of racing on the same schema. ADR-0005 puts services on Azure Container Apps, which scales to zero and can cold-start several replicas simultaneously, and EF Core's `Migrate()` is not safe to run concurrently.

## Considered Options

**A separate migration step** (a one-off job in the deploy pipeline, with services never migrating) is what a production system would more likely do: it decouples schema changes from application startup and makes rollbacks legible. It was rejected *for now* only because no deployment pipeline exists yet — deployment is explicitly out of scope for the Catalog spec — and adopting it would mean the test seam has to apply migrations itself. This is the natural thing to revisit when deployment work starts.

**Leaving the race in place** was rejected: it is a real defect under the chosen hosting model, not a theoretical one.

## Consequences

- An unreachable database still kills the process at startup, so `/health` cannot report the database unhealthy at boot — the health check's failure path only covers a database that dies after a successful start. Accepted deliberately: a service that cannot reach its own database has nothing useful to serve.
- Each service must choose its own distinct lock key; reusing another service's key would serialise unrelated deployments.
