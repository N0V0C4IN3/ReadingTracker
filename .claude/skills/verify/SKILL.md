---
name: verify
description: Verify a change to ReadingTracker's web frontend by driving the running app in a phone-sized browser. Includes the phone keyboard check every change to a text field, sheet or orientation must pass.
---

# Verifying ReadingTracker

The surface is the Blazor WebAssembly frontend in a browser. Drive it there; do not run the test suite.

## Handle

1. Rebuild and start the local stack, then wait for the web container (the first request after `up` returns 000 for a few seconds):
   `docker compose --profile services up --build -d`, then `curl -s -o /dev/null -w "%{http_code}" http://localhost:5200/` until 200.
2. Chrome DevTools: `emulate` with `390x844x3,mobile,touch` (`,landscape` for a phone on its side), then `navigate_page` to `http://localhost:5200/` with `ignoreCache`.
3. Sign in without editing config. `appsettings.Development.json` may have `DevSignIn.Enabled` false locally, and that file is the user's; pass this as `initScript` on the navigation and the page signs in as the dev reader (`Account: Dev Reader`). It only rewrites a successful response, so a missing file still fails as a missing file:

```js
const of = window.fetch.bind(window);
window.fetch = async (input, init) => {
  const url = typeof input === 'string' ? input : (input.url || String(input));
  const r = await of(input, init);
  if (!url.includes('appsettings.Development.json') || !r.ok) return r;
  const t = await r.text();
  return new Response(t.replace(/"Enabled"\s*:\s*false/, '"Enabled": true'),
    { status: 200, headers: { 'content-type': 'application/json' } });
};
```

The shelf shows ghost cards for a few seconds while Library answers; wait for real cards (poll for `.search__fab` or a book link) before driving.

## Flows worth driving

- **Shelf cards**: Want to Read (meta line, Start reading), Reading (figure and bar), Finished (Read and its date).
- **Settings → Find a different one → a result**: the details sheet, Use this edition, on desktop and at phone width.
- **A phone text field, or what decides a phone's screen**: the keyboard check in `docs/agents/mobile-keyboard-check.md`.

## Gotchas

- The pi runs only the backend; the frontend is Azure Static Web Apps, deployed by `deploy-web.yml` on a push to `master`. Its address is not in the repo.
