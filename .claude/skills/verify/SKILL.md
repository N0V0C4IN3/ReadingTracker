---
name: verify
description: Verify a change to ReadingTracker's web frontend by driving the running app in a phone-sized browser. Includes the phone keyboard check every change to a text field, sheet or orientation must pass.
---

# Verifying ReadingTracker

The surface is the Blazor WebAssembly frontend in a browser. Drive it there; do not run the test suite. A change to what is on screen is verified when you have looked at it: a screenshot read, not a diff reasoned about.

## Without Chrome DevTools

When the `chrome-devtools` tools are missing or failed to connect, ask the user to reconnect them with `/mcp`, and meanwhile look with `tools/dev/shot.mjs`: it drives the Chrome installed here, signs in as the dev reader, and saves phone and desktop PNGs to `artifacts/shots/<commit>/` for you to Read. Its header has the install line and the flags; `--click` repeats for a flow.

```bash
node tools/dev/shot.mjs --click ".entry__title a" --click ".days__all" --name history
```

## Handle

1. Rebuild the local stack and wait until it answers: `bash tools/dev/up.sh` (a few minutes; give it a 10-minute timeout). It exits 0 at "ready", or names the service that didn't come up.
2. Chrome DevTools: `emulate` with `390x844x3,mobile,touch` (`,landscape` for a phone on its side), then `navigate_page` to `http://localhost:5200/` with `ignoreCache`.
3. Sign in without editing config: pass the text of `tools/dev/dev-sign-in.js` as `initScript` on the navigation (its header says why), then press **Sign in (dev)** on the form the page offers.

The shelf shows ghost cards for a few seconds while Library answers; wait for real cards (poll for `.search__fab` or a book link) before driving.

## Flows worth driving

- **Anything that lists a reader's days (History, stats, the calendar)**: give the dev reader a year of reading first, or a list too short to fill its box hides what a long one does. `docker compose exec -T postgres sh -lc 'psql -U $POSTGRES_USER -d $POSTGRES_DB -v reader=dev-reader' < tools/dev/seed-reading.sql` (local only; `unseed-reading.sql` takes it away). Then open a book and press Show more.
- **Shelf cards**: Want to Read (meta line, Start reading), Reading (figure and bar), Finished (Read and its date).
- **Settings → Find a different one → a result**: the details sheet, Use this edition, on desktop and at phone width.
- **A phone text field, or what decides a phone's screen**: the keyboard check in `docs/agents/mobile-keyboard-check.md`.

## Gotchas

- Checking the deployed app rather than the local one: the addresses, and which half ships how, are in `docs/deployment.md`.
