## Agent skills

### Issue tracker

Issues live in GitHub Issues on this repo, via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### Writing web frontend code

Before adding a helper, component, script or style to `ReadingTracker.Web`, check `docs/agents/web-frontend.md`. It maps each job to the piece that already does it.

### Looking at a change

A change to what the web frontend shows is looked at before it is called reviewed, verified or ready to check: `/verify` drives the app. One that touches a text field on a phone, or what decides a phone's screen (search, sheets, orientation), also passes the keyboard check in `docs/agents/mobile-keyboard-check.md`.

### Pop-ups, menus and sheets

Anything that opens over the page matches the existing ones: frosted glass, the shared scrim and layers, a draggable sheet from the foot on a phone, the same arrive/leave animations, fonts and status colours. Read `docs/agents/ui-overlays.md` before adding or changing one.
