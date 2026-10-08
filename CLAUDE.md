## Agent skills

### Issue tracker

Issues live in GitHub Issues on this repo, via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### Writing web frontend code

Before adding a helper, component, script or style to `ReadingTracker.Web`, check `docs/agents/web-frontend.md`. It maps each job to the piece that already does it.

### Looking at a change

A change to what the web frontend shows is looked at before it is called reviewed, verified or ready to check: `/verify` drives the app, and `tools/dev/shot.mjs` takes phone and desktop screenshots when the Chrome DevTools server is not connected.

### Reviewing a change to a phone text field

A change that touches a text field on a phone, or what decides a phone's screen (search, sheets, orientation), is checked for the keyboard in the browser before it is called reviewed or verified. Read `docs/agents/mobile-keyboard-check.md`; `/verify` has the handle for driving the app.

### Pop-ups, menus and sheets

Anything that opens over the page matches the existing ones: frosted glass, the shared scrim and layers, a draggable sheet from the foot on a phone, the same arrive/leave animations, fonts and status colours. Read `docs/agents/ui-overlays.md` before adding or changing one.
