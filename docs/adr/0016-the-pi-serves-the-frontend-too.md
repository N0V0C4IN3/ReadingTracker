# The Pi serves the frontend too

ADR-0012 moved everything but the frontend onto the Raspberry Pi and left the Blazor WebAssembly
app on Azure Static Web Apps, whose Free plan costs nothing. It costs nothing only while the
subscription under it is in good standing, though. That subscription was a free trial, and when
the trial ended on 8 October 2026 Azure stopped serving the app. Every path, Azure's own
`/.auth/me` included, answered with its 404 page, while the services on the Pi stayed healthy and
nobody could reach them. Keeping the app on Azure would mean upgrading the subscription to
pay-as-you-go and putting a card behind a project whose one hard constraint is spending nothing.

## Decision

The Pi serves the frontend. A `web` service in `docker-compose.pi.yml` publishes the app and hands
it out from nginx. It is also the stack's front door: Tailscale Funnel sends every request to it,
and it passes `/api/` and `/health` on to the Gateway. Every Gateway route already sat under
`/api/`, so nothing had to move to make room.

The app and the API are now **one origin**, `https://readingtracker.tail03af11.ts.net`. The browser
makes no cross-origin calls, `connect-src` needs only `'self'` for the Gateway, and the Gateway's
`AllowedOrigins` is that same address, written into the compose file rather than kept in `.env`.
The KOReader plugin's address does not change.

What Static Web Apps did, nginx now does (`deploy/pi/web/`): it falls back to `index.html` for
the app's routes, sends the security headers with `X-Frame-Options` per route, sends the
Content-Security-Policy with every inline script allowed by hash (the hashes are computed at image
build time, as the workflow used to compute them), marks `_framework/` immutable, and serves the
`.gz` files the publish already wrote.

## Consequences

- **Merging no longer deploys the frontend.** It ships with the services, by hand, on the Pi.
  `deploy-web.yml` now runs only by hand, so that a merge does not upload to an app Azure no
  longer serves.
- One host now serves everything, so the Pi or its Funnel going down takes the app down with the
  API. With the API down the app was no use anyway.
- Google's OAuth client has to list the Pi's address as a JavaScript origin, and its
  `/authentication/login-callback` as a redirect URI.

## Considered Options

**Upgrade the Azure subscription.** The Free Static Web App would still cost nothing, and nothing
in the repository would change. Rejected: it puts a payment method behind a project that has
none, and only a spending limit stands between that card and the next resource someone creates
by mistake.

**GitHub Pages.** Free and hands-off. Rejected: it cannot set response headers, so the
Content-Security-Policy and the per-route `X-Frame-Options` would be lost. The app would still be
on a second origin, too.

**Cloudflare Pages.** Free, sets headers from a `_headers` file, and deploys on a push, much like
Static Web Apps. It is still a second account and a second origin to keep, plus a CORS allowance
and an OAuth origin. Not rejected so much as not needed: the Pi was already running all day.
