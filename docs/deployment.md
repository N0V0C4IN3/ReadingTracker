# Deploying ReadingTracker

## What is deployed today

Everything runs on a Raspberry Pi, reached through Tailscale Funnel at
**https://readingtracker.tail03af11.ts.net**: the frontend, the services, Postgres and the broker
([ADR-0012](adr/0012-self-host-on-a-raspberry-pi-behind-tailscale-funnel.md),
[ADR-0016](adr/0016-the-pi-serves-the-frontend-too.md)). It ships by hand. Secrets are in an
untracked `.env` beside `docker-compose.pi.yml` on the Pi.

To deploy, on the Pi, in the repository's checkout there:

```sh
git pull
docker compose -f docker-compose.pi.yml up --build -d
curl -s https://readingtracker.tail03af11.ts.net/health   # through the Funnel, as visitors reach it
```

Funnel sends every request to the `web` service. That is nginx: it serves the published frontend
and passes `/api/` and `/health` on to the Gateway, so the app and the API share one origin. Its
configuration is in `deploy/pi/web/`. `tools/dev/up.sh` never runs it (the local `web` is
`dotnet run`), so to try a change to it, build it beside the running local stack and open
http://localhost:5300:

```sh
docker build -f deploy/pi/web/Dockerfile --build-arg PUBLIC_ORIGIN=http://localhost:5300 -t readingtracker-web-pi .
docker run --rm --network readingtracker_default -p 5300:80 readingtracker-web-pi
```

If a deploy changes `deploy/pi/tailscale-serve.json`, also run
`docker compose -f docker-compose.pi.yml restart tailscale`. The sidecar reads that file when it
starts.

The maintainer's `deploy-to-pi` skill and agent do this, and check the disk and the stack, but
they live in a personal Claude setup, not in this repository.

The one address is set in one place: `FUNNEL_HOST` in the Pi's `.env`, defaulting to the address
above. The compose file builds it into the frontend as its Gateway address and gives it to the
Gateway as its allowed origin. Outside the repository, Google's OAuth client lists the address
among its JavaScript origins, and its `/authentication/login-callback` among its redirect URIs.

A merge deploys nothing. Frontend and services go out together, in the one `up --build`.

Locally, the stack is `docker-compose.yml`: `bash tools/dev/up.sh` rebuilds it and waits until http://localhost:5200 answers.

## Why the frontend needs a generated settings file

A WebAssembly app is static files executing in someone else's browser. It has no environment
variables to read at start-up, so anything deployment-specific has to be written into a file that
ships alongside it. `wwwroot/appsettings.json` carries the local gateway address —
`http://localhost:5100/` — and a published build with nothing overriding it loads perfectly and
then cannot reach a single service.

`deploy/pi/web/Dockerfile` writes `wwwroot/appsettings.Production.json` before publishing, from
the `PUBLIC_ORIGIN` build argument the compose file passes it, and refuses to build if that is not
an `https` address (or `http://localhost:<port>`, to try the image locally). Nothing secret goes in it, because nothing there can be: the file is served to every
visitor. The Google client id is public by design — it identifies the application, it does not
authenticate it.

## The Content-Security-Policy, and why the build finishes it

nginx sends a Content-Security-Policy with every page. `deploy/pi/web/csp.py` writes it while the
image builds, after `dotnet publish`, because the inline scripts' hashes are not known before
then: Blazor writes the import map into `index.html` at publish time, fingerprinted, so its hash
changes with every build that touches a framework file. The script hashes every inline `<script>`
it finds rather than pinning hashes, so editing one of the page's own scripts cannot silently break
the deployed site. (A local `dotnet run` sends no policy at all.) The other headers are in
`deploy/pi/web/headers.conf` and `nginx.conf`.

What each source is for, so the next origin goes in the right place:

- `script-src 'self' 'wasm-unsafe-eval' <hashes>` — the app's own scripts, the .NET runtime
  (WebAssembly needs `wasm-unsafe-eval`), and the page's inline scripts by hash. No
  `unsafe-inline`, no other origin.
- `style-src 'self' 'unsafe-inline'` — components set `style` attributes (arrival delays, the
  loading bar), which counts as inline style. Blocking that would need every one rewritten as a
  class; inline *style* is a far smaller risk than inline script, so it is allowed.
- `img-src 'self' https:` — book jackets come from whichever provider had the book, and from
  hand-entered cover URLs, so any `https` origin; never `http`, never `data:`.
- `connect-src` — `'self'`, which is the Gateway too, and Google's two endpoints the sign-in library fetches
  (discovery at `accounts.google.com`, signing keys at `www.googleapis.com`).
- `frame-src 'self' https://accounts.google.com` — the hidden iframe that asks Google whether a
  session already exists, which Google answers by redirecting back to this origin.
- No `frame-ancestors`, on purpose: who may frame *this* site is `X-Frame-Options`' job, and it
  is already per route — `DENY` everywhere but the sign-in callback, which the iframe above
  needs `SAMEORIGIN` for. A CSP directive here would override that for every route at once.
- `upgrade-insecure-requests` — some providers still hand out `http://` jacket URLs.

Adding a provider that serves covers means nothing; adding one the *browser* has to call means
its origin in `connect-src`. `Permissions-Policy` turns off camera, microphone, geolocation and
payment, none of which this application has any use for.

## Known rough edges

**Cold start.** The frontend takes around eight seconds to first paint on a published Release
build, and it was not the download — on Azure's CDN the framework arrived in about 250ms and the
rest was the WebAssembly runtime starting. The Pi serves it over its home upload link, so that
figure is now worth measuring again. Ahead-of-time compilation is the lever, at the cost of a
substantially larger download. Unmeasured against this deployment; worth doing before it is
worth arguing about.

**No smoke test of the whole path.** Each deploy checks the thing it deployed — the settings file
for the frontend, `/health` for the services — and none signs in and loads a shelf. That
gap is the deployment-shaped version of the one [#59](https://github.com/N0V0C4IN3/ReadingTracker/issues/59)
describes.

**The Funnel ingress goes dark.** Seen once so far: after the tailscale sidecar lost its control
connection (`PollNetMap: unexpected EOF` in its log), Tailscale's ingress relays stopped forwarding
to the node while everything on the Pi reported healthy — `tailscale funnel status` still said
"Funnel on", and the hostname still answered from the Pi itself because MagicDNS resolves it to
the tailnet address, not the ingress. Visitors got a failed TLS handshake and the frontend said
ReadingTracker could not be reached. `docker compose -f docker-compose.pi.yml restart tailscale`
fixed it in seconds. The `funnel-watch` service in the compose file now does that unattended: it
probes `/health` through public DNS (so through the ingress) once a minute and restarts the sidecar
after three misses; `docker compose logs funnel-watch` shows every miss and restart.

**A silent broker.** `/health` does not cover RabbitMQ, deliberately: a service that cannot reach
the broker is still able to serve every request a reader makes, so reporting it unhealthy would
take a working service out of rotation over a degraded feature. The cost is that a broken
broker connection is invisible to every automated check there is. Adding a book and watching it
reach the shelf is the manual test; a `degraded` health status that nothing acts
on is the fix, if this ever bites.

## Google sign-in

Beside the address at the top of this page, the OAuth client lists `http://localhost:5200` for
the local stack. A new address for the app needs both its entries added, or sign-in fails from it
with `redirect_uri_mismatch` while still working locally.

Dev sign-in cannot be turned on in production and needs nothing done to it. It requires both the
Development environment *and* an explicit flag, independently, on the frontend and on the Gateway
([ADR-0008](adr/0008-dev-sign-in-bypasses-google-when-explicitly-enabled.md),
[ADR-0009](adr/0009-the-frontend-swaps-google-out-for-dev-sign-in.md)) — configuration alone
cannot reach it.

## Azure, retired

The services ran on Azure Container Apps, with Postgres on Neon and the broker on CloudAMQP
([ADR-0005](adr/0005-deployment-stack.md)), until
[ADR-0012](adr/0012-self-host-on-a-raspberry-pi-behind-tailscale-funnel.md). The frontend was on
Azure Static Web Apps until [ADR-0016](adr/0016-the-pi-serves-the-frontend-too.md). The
subscription's free trial ended on 8 October 2026, and everything left in Azure was deleted the
same day: the resource group, the identity GitHub Actions signed in with and the repository's
`AZURE_*` variables for it, the Static Web Apps deploy token secret, and the two workflows,
`deploy-services.yml` and `deploy-web.yml`.

There is no way back to keep working. Should one be wanted, the workflows and the setup this page
used to walk through (provider registration, the container apps' settings, Neon's direct
endpoint, the OIDC credential) are in git history at `83a868c`, the last commit with both.
