# Deploying ReadingTracker

> **Most of this page describes the old deployment.**
> [ADR-0012](adr/0012-self-host-on-a-raspberry-pi-behind-tailscale-funnel.md) moved the services,
> Postgres and the broker onto a Raspberry Pi reached through Tailscale Funnel. What is deployed
> today is `docker-compose.pi.yml` on that Pi, with secrets in an untracked `.env` beside it.
>
> **The frontend half of this page is still current**: it stays on Azure Static Web Apps and
> `deploy-web.yml` still ships it on every push to master. Only `WEB_GATEWAY_BASE_ADDRESS` has
> changed, to the Pi's `ts.net` address.
>
> Everything below about Container Apps, Neon and CloudAMQP is kept because it is the way back.
> `deploy-services.yml` still works and is still configured; it simply no longer runs on a push,
> so nothing rebuilds a deployment nobody is using. Run it by hand to return to Azure, and point
> `WEB_GATEWAY_BASE_ADDRESS` at the Container Apps gateway again.

The stack this page describes was fixed by [ADR-0005](adr/0005-deployment-stack.md): the three
services to Azure Container Apps, Postgres to Neon, the WebAssembly frontend to Azure Static Web
Apps.

Two workflows do the deploying, both on a push to `master` and both runnable by hand from the
Actions tab:

- `.github/workflows/deploy-services.yml` — builds Catalog, Library and Gateway images, pushes
  them to this repository's GHCR namespace, updates the container apps, then asks each one's
  `/health` endpoint from outside before calling it done.
- `.github/workflows/deploy-web.yml` — writes `appsettings.Production.json`, publishes the
  frontend and uploads it.

Neither will run to completion until the resources below exist and the repository is configured.
Both fail on the first step with a message naming what is missing, rather than deploying
something half-configured.

## Why the frontend needs a generated settings file

A WebAssembly app is static files executing in someone else's browser. It has no environment
variables to read at start-up, so anything deployment-specific has to be written into a file that
ships alongside it. `wwwroot/appsettings.json` carries the local gateway address —
`http://localhost:5100/` — and a published build with nothing overriding it loads perfectly and
then cannot reach a single service.

`deploy-web.yml` writes `wwwroot/appsettings.Production.json` from repository variables before
publishing, and then greps the published output to make sure the result does not still say
localhost. Nothing secret goes in it, because nothing there can be: the file is served to every
visitor. The Google client id is public by design — it identifies the application, it does not
authenticate it.

## What to create

### 1. Neon

One project, one database. Both Catalog and Library use the same instance and keep to their own
schema ([ADR-0004](adr/0004-schema-per-service-shared-postgres.md)), so one connection string
serves both.

Choose the Azure region first and put Neon in whichever of its regions is geographically nearest.
Every query crosses from Azure to whichever cloud Neon sits in, and a region is the one decision
here that is expensive to revisit — moving a project later means a dump and restore, while
everything else is a connection string. Neon ran in Azure regions until April 2026 and no longer
does, so this really is cross-cloud; Frankfurt to Frankfurt (Azure Germany West Central against
Neon's AWS `eu-central-1`) is about as close as the two get in Europe, and Germany West Central
is confirmed available for Container Apps. Each service migrates itself on boot under an advisory lock
([ADR-0006](adr/0006-serialize-migrations-with-an-advisory-lock.md)) — there is no migration step
in the pipeline and there should not be one.

> **Use the direct endpoint, not the pooled one.** Neon offers both; the pooled host has
> `-pooler` in its name and is PgBouncer in transaction pooling mode. `pg_advisory_lock` is a
> *session* lock, and the migration code deliberately pins the lock and the migration to one
> connection because of that. Under transaction pooling a client connection is only tied to a
> backend for the length of a transaction, so the lock would be taken on one backend, the
> migration would run on another without holding it, and the unlock would quietly fail on a
> third — leaving the lock stranded until that backend's session ends.
>
> Nothing would error. Two replicas cold-starting together would simply both migrate, which is
> the exact race ADR-0006 exists to prevent, and the failure would surface later as a corrupted
> migration history rather than as a connection problem.

Two further things on the connection string:

- `SSL Mode=Require` — Neon will not accept a plaintext connection. Its certificate chains to a
  public CA, so there is no need to disable verification with `Trust Server Certificate`.
- `Maximum Pool Size=10` or thereabouts. Npgsql defaults to **100 connections per pool**, and
  that is per service, per replica. Two services cold-starting a few replicas each will exhaust
  a free-tier Neon compute long before they need that many.

### 2. The broker

A managed shared instance — CloudAMQP's free tier — rather than a RabbitMQ container we run
ourselves ([ADR-0011](adr/0011-rabbitmq-runs-as-a-managed-shared-instance.md)). Create an
instance and copy its URL.

It will be an `amqps://` URL, and that needs nothing done to it. Both services build their
connection from a URI and RabbitMQ.Client enables TLS from the scheme on its own, so the whole
change is the connection string. The same value goes to both Catalog and Library: one publishes
book events, the other consumes them, and they have to be on the same broker to be talking at
all.

If it is wrong or missing, nothing crashes. Publishing is wrapped, so an unreachable broker is
logged and stepped over and every synchronous path keeps working — which is precisely why it is
worth checking rather than assuming. After deploying, add a book and confirm it appears on the
shelf; that is the path the events carry.

### 3. Azure

A fresh subscription has none of the resource providers registered, and the failure is not
obvious — `az containerapp list` reports the subscription "is not registered for the
Microsoft.App resource provider" rather than saying anything about the resource you were trying
to create. Registration is free, creates nothing and is permanent, but it propagates in the
background, so start it before anything else:

```
az provider register -n Microsoft.App --wait
az provider register -n Microsoft.Web --wait
az provider register -n Microsoft.ContainerRegistry --wait
az provider register -n Microsoft.OperationalInsights --wait
```

`Microsoft.OperationalInsights` is the one nobody expects: a Container Apps environment creates a
Log Analytics workspace for itself, and without the provider the environment fails to create at
all.

A resource group, a Container Apps environment, and three container apps named
`<prefix>-catalog`, `<prefix>-library`, `<prefix>-gateway` — the workflow builds those names from
`CONTAINER_APP_PREFIX`, so they have to follow that shape. Ingress: external on the gateway,
internal on the other two, which is what makes
[ADR-0007](adr/0007-services-trust-a-reader-identity-header-from-the-gateway.md) safe — Catalog
and Library trust a reader-identity header, so nothing but the gateway may be able to set it.

Then a Static Web App for the frontend. It cannot go in the same region as everything else: Static
Web Apps exists in five regions only — Central US, East US 2, West US 2, West Europe, East Asia —
and no amount of matching will put it next to the services. This does not matter. The region is
where the resource record lives; the files are served from a CDN edge near the visitor, and a
WebAssembly app calls the gateway from the reader's browser, so nothing a reader waits for passes
through the Static Web App's region at all. West Europe against services in Germany West Central
is fine.

Set each service's own configuration **on the container app in Azure**, not in the workflow. The
pipeline only ever changes the image, deliberately, so that a deployment cannot revert a setting
someone changed in the portal to put out a fire.

| Service | Needs |
| --- | --- |
| Catalog | `ConnectionStrings__CatalogDb`, `RabbitMq__ConnectionString`, `GOOGLE_BOOKS_API_KEY` |
| Library | `ConnectionStrings__LibraryDb`, `RabbitMq__ConnectionString`, `Catalog__BaseAddress` |
| Gateway | `Google__ClientId`, `AllowedOrigins__0`, `ReverseProxy__Clusters__catalog__Destinations__primary__Address`, `ReverseProxy__Clusters__library__Destinations__primary__Address` |

Two of the Gateway's are easy to miss and both stop it starting rather than letting it come up
half-configured:

- `Google__ClientId` lives only in `appsettings.Development.json`, because a deployment was
  always meant to supply its own. Without it the Gateway would have nothing to check a token's
  audience against, and would accept tokens issued to any Google application in the world — so
  it refuses to start instead. Unless you have made a separate OAuth client for production, this
  is the same client id already committed in that file.
- `AllowedOrigins` has no default either, rather than coming up with CORS no browser can pass.

The two `ReverseProxy` addresses are the internal FQDNs of the Catalog and Library container apps,
with a trailing slash. Routing itself is committed in `appsettings.json`; only the addresses are
environmental.

`Catalog__BaseAddress` on Library is the one to be careful about, because it is the opposite: it
has a default, and the default is `http://localhost:5103/`. Library calls Catalog directly for
book details rather than going back out through the Gateway, so left unset it starts cleanly,
passes its health check, and fails only when a reader opens a shelf. Its own internal FQDN, with
a trailing slash.

`GOOGLE_BOOKS_API_KEY` is the flat spelling container platforms and `.env` files conventionally
use; Catalog accepts it as an alias for `GoogleBooks__ApiKey`, and either works.

One thing every connection string to Postgres needs on these images: `Gss Encryption Mode=Disable`.
Npgsql 10 attempts GSSAPI first, and the .NET runtime images have shipped without Kerberos
libraries since .NET 8, so without it every start logs a `libgssapi_krb5.so.2: cannot open shared
object file` before falling back. Harmless, and exactly the sort of noise a real error goes
unnoticed in.

### 4. Google

Add the Static Web Apps URL to the OAuth client's **Authorised JavaScript origins**, and
`https://<the-app>/authentication/login-callback` to its **Authorised redirect URIs**. Until this
is done, sign-in fails from the deployed site while working locally, because the only origin
currently registered is `http://localhost:5200`.

Dev sign-in cannot be turned on in production and needs nothing done to it. It requires both the
Development environment *and* an explicit flag, independently, on the frontend and on the Gateway
([ADR-0008](adr/0008-dev-sign-in-bypasses-google-when-explicitly-enabled.md),
[ADR-0009](adr/0009-the-frontend-swaps-google-out-for-dev-sign-in.md)) — configuration alone
cannot reach it.

### 5. This repository

Azure is reached by OIDC federated login, so there is no long-lived Azure secret. Create an app
registration, give it Contributor on the resource group, and add a federated credential for this
repository on the `master` branch.

**Variables** (Settings → Secrets and variables → Actions → Variables):

| Name | Example |
| --- | --- |
| `AZURE_CLIENT_ID` | the app registration's client id |
| `AZURE_TENANT_ID` | the directory id |
| `AZURE_SUBSCRIPTION_ID` | the subscription id |
| `AZURE_RESOURCE_GROUP` | `readingtracker` |
| `CONTAINER_APP_PREFIX` | `readingtracker` |
| `WEB_GATEWAY_BASE_ADDRESS` | `https://readingtracker-gateway.<region>.azurecontainerapps.io/` |
| `WEB_GOOGLE_CLIENT_ID` | only if production uses a different OAuth client from the committed one |

**Secrets**:

| Name | Where it comes from |
| --- | --- |
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | the Static Web App's deployment token |

## Order

The frontend needs the gateway's address, so the services go first.

1. Register the Azure resource providers, and — while they propagate — create the Neon database
   and the CloudAMQP instance.
2. Create the Azure resources and set each container app's configuration.
3. Set the variables and the secret here.
4. Run **Deploy services**. It ends by checking `/health` from outside.
5. Put the gateway's URL in `WEB_GATEWAY_BASE_ADDRESS`, and the Static Web App's URL in the
   Gateway's `AllowedOrigins__0` and in the Google OAuth client.
6. Run **Deploy frontend**.

## Known rough edges

**Cold start.** The frontend takes around eight seconds to first paint on a published Release
build, and it is not the download — the framework arrives in about 250ms and the rest is the
WebAssembly runtime starting. Ahead-of-time compilation is the lever, at the cost of a
substantially larger download. Unmeasured against this deployment; worth doing before it is
worth arguing about.

Container Apps scale to zero, so the first request after an idle period also pays for a container
starting. The health check in the services workflow is patient about this on purpose.

**No smoke test of the whole path.** Both workflows check the thing they deployed — `/health` for
the services, the settings file for the frontend — and neither signs in and loads a shelf. That
gap is the deployment-shaped version of the one [#59](https://github.com/N0V0C4IN3/ReadingTracker/issues/59)
describes.

**A silent broker.** `/health` does not cover RabbitMQ, deliberately: a service that cannot reach
the broker is still able to serve every request a reader makes, so reporting it unhealthy would
take a working service out of rotation over a degraded feature. The cost is that a broken
broker connection is invisible to every automated check there is. Adding a book and watching it
reach the shelf is the manual test; a `degraded` health status that Container Apps does not act
on is the fix, if this ever bites.
