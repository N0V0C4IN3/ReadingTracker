# Schema-per-service within one shared Postgres instance

Each service gets its own database/schema and never queries another service's tables directly — but all schemas live on one physical Postgres instance rather than fully separate instances per service. This trades away true physical isolation (what a production microservices system would likely have) for lower operational cost, while still proving the data-ownership boundary that matters for the architecture story.
