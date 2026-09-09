# RabbitMQ runs as a managed shared instance, not as a container we operate

[ADR-0003](0003-rabbitmq-for-cross-service-events.md) put RabbitMQ between Catalog and Library.
[ADR-0005](0005-deployment-stack.md) then chose hosting for the services, the database and the
frontend and said nothing about the broker, which left the one piece of the system with no home —
discovered while writing the deployment pipeline, not while designing it.

The broker is a managed shared instance from a provider with a permanent free tier (CloudAMQP's
shared plans are the ones that fit), reached over `amqps://`. Both services already build their
connection from a URI — `new ConnectionFactory { Uri = new Uri(connectionString) }` — and
RabbitMQ.Client turns on TLS from the `amqps` scheme by itself, so this is a configuration value
and no code.

## Considered Options

**RabbitMQ as a container app in the same environment**, which keeps everything in one place and
one bill, was rejected on what it costs to make it a broker rather than a process that happens to
be running. It cannot scale to zero: a broker that shuts down when idle loses whatever had not
been consumed, so it needs a replica pinned on permanently, which is exactly the always-on
compute the free grant is thin on. It needs a mounted volume for durable queues, TCP ingress
rather than HTTP, and it makes us the people who upgrade RabbitMQ. That is a lot of operating
surface for two services exchanging book events.

**Azure Service Bus** was rejected because it is not RabbitMQ. It would mean rewriting the
publisher and the consumer to a different client and a different delivery model, to solve a
problem that is only "where does this run".

**A Container Apps add-on** would have been the simplest answer — a broker inside the environment
with no account anywhere else. The add-ons were a public preview and were
[retired on 30 September 2025](https://learn.microsoft.com/azure/container-apps/services).

**Dropping the broker for the demo** is survivable and was seriously considered: publishing is
wrapped, so an unreachable broker is logged and stepped over, and everything a reader touches
keeps working. It was rejected because what stops is invisible. Catalog and Library quietly stop
agreeing, and a system that is half-wired in production while looking healthy is worse than one
that is honestly missing a feature.

## Consequences

- The broker is the one piece of infrastructure outside Azure other than the database. Two free
  accounts rather than one; both are permanent tiers rather than trials.
- Shared plans are metered — a monthly message allowance, a channel limit, a ceiling on unacked
  messages, and a cap on new connections per second per IP. Catalog publishing book events and
  Library consuming them is nowhere near any of them, but a future feature that puts a message on
  the bus per reader action is the thing that would find the ceiling first.
- Cross-cloud adds latency to publishing. It is not on any path a reader waits for: publishing is
  already fire-and-forget and already tolerates the broker being gone entirely.
- Nothing about this is load-bearing for correctness. If the free tier is outgrown or withdrawn,
  the replacement is a connection string.
