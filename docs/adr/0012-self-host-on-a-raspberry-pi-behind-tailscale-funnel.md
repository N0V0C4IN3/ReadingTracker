# ReadingTracker self-hosts on a Raspberry Pi behind Tailscale Funnel

The services, Postgres and RabbitMQ run as the existing `docker-compose.yml` stack on a Raspberry
Pi 5 we already own, reached from the internet through Tailscale Funnel. The Blazor WebAssembly
frontend stays on Azure Static Web Apps.

This supersedes [ADR-0005](0005-deployment-stack.md) for compute, the database and the broker, and
keeps its choice of frontend hosting. It also retires [ADR-0011](0011-rabbitmq-runs-as-a-managed-shared-instance.md):
the broker no longer needs a home of its own, because it now has one.

ADR-0005's reasoning was sound and its rejections were re-verified in September 2026 — Fly.io,
Railway and Render have all got *worse*, not better. What broke was its pairing rather than its
judgement. Neon left Azure's regions in April 2026, so the colocated compute-and-database unit it
chose stopped existing, and every query began crossing from Azure to AWS. Scale-to-zero then meant
a reader's first visit after an idle period woke three containers in series and resumed a suspended
Postgres before anything was served.

The obvious fix was to keep one replica warm, and it is not available: the Container Apps free
grant is per *subscription* and stretches to roughly 200 replica-hours a month, while a single
always-on replica at the smallest allocation needs about 3.6× that — around $5.62 a month, or $21
for all three services. Spending nothing is a hard constraint here, so warmth had to come from
somewhere other than a cloud bill.

Hardware we already own supplies it. The Pi is a 16 GB Pi 5 already running Docker for two other
projects at near-zero load, and nothing on it scales to zero, so **cold start stops existing rather
than getting smaller**. Postgres, RabbitMQ, Catalog and Library all land on one host, which
collapses every internal hop to the Docker bridge and retires the cross-cloud round trip entirely.

## Considered Options

**Staying on Container Apps and paying for a warm replica** is the smallest change and costs about
$5.62 a month. Rejected because zero spend is the constraint this decision exists to satisfy, not a
preference to trade away.

**Oracle Cloud Always Free** fits an always-on 2 OCPU / 12 GB Arm instance inside its grant with
about 4% to spare, and would have removed cold start the same way. Rejected as second choice: it
requires a card on file and an irreversible home-region decision, to rent hardware that never
sleeps when we already own some. It remains the fallback if the Pi arrangement fails.

**Google Cloud Run** was rejected on arithmetic. Its free grant is byte-for-byte identical to
Container Apps' — the same 180,000 vCPU-seconds and 360,000 GiB-seconds — so changing provider buys
no allowance and no colocation, and its European egress is not free-tier covered, which is a live
bill risk under a zero-spend rule.

**Cloudflare Tunnel rather than Tailscale Funnel** is the better long-term ingress: it supports a
custom domain, which Funnel cannot, and its edge caching would serve the WebAssembly payload from
near the visitor. It is deferred rather than rejected. Funnel is already installed, already running
and already serving another project from this box, so it costs nothing to adopt and nothing to
abandon. Cloudflare is the upgrade if Funnel's undisclosed bandwidth limits bite or the hostname
starts to matter.

**Moving the frontend onto the Pi as well** would have put the whole system in one place, which was
the original instinct. Rejected because it works directly against the goal: Static Web Apps is
free, globally distributed, and serves the pre-compressed Brotli assets a Blazor publish emits,
while a multi-megabyte payload sent over a home upload link through a relay is the slowest
arrangement available. The frontend is also implicated in none of the latency this change targets.

## Consequences

- **When the Pi is off, the app is gone, and that is accepted deliberately.** Home power and
  broadband are now the availability story; there is no failover and nothing watches for downtime.
  This is a portfolio demo rather than a service with users, and the trade was made explicitly
  rather than discovered.
- Nothing scales to zero, so there is no cold start to instrument, disguise or pay to avoid. The
  keep-warm question disappears with it.
- Neon and CloudAMQP are retired. Two fewer free tiers to keep alive, and the `postgres` and
  `rabbitmq` services in `docker-compose.yml` — which have existed for local development all along
  — become the real thing.
- [ADR-0006](0006-serialize-migrations-with-an-advisory-lock.md)'s warning about pooled endpoints
  stops applying, since a local Postgres has no PgBouncer in front of it. The advisory lock itself
  stays: it costs nothing and the reasoning survives a change of host.
- The public URL becomes `readingtracker.<tailnet>.ts.net`. Funnel cannot use a custom domain and
  the tailnet suffix is randomly assigned, so this is a worse address than the one being replaced.
- Funnel is a relay. Traffic goes visitor → Tailscale ingress → Pi, and is *"subject to
  non-configurable bandwidth limits"* that Tailscale documents but does not quantify. This is the
  most likely reason to revisit the ingress choice.
- Google sign-in is unaffected. `ts.net` is on the Public Suffix List, submitted by Tailscale, so
  `<tailnet>.ts.net` is a top private domain — structurally identical to the
  `6.azurestaticapps.net` subdomain the app already authenticates against today, which is on the
  same list for the same reason. Only the registered origins change.
- Deployment changes shape: GHCR plus `az containerapp update` gives way to a build-and-deploy over
  SSH, and images must be `linux/arm64`. Secrets move from Container Apps to an untracked `.env`
  on the Pi.
- The Pi hosts unrelated stacks on one Docker daemon, so this project's containers, volumes and
  disk usage are now a shared concern rather than an isolated one.
