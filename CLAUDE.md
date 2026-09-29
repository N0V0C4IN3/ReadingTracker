## Agent skills

### Issue tracker

Issues live in GitHub Issues on this repo, via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### Reviewing a change to a phone text field

Any change that touches a text field on a phone, or what decides a phone's screen (search, sheets, orientation), is checked in the browser for the keyboard before it is called reviewed or verified (`/review`, `/verify`): it opens with the cursor in the box, and it stays up when the window shrinks for it. Read `docs/agents/mobile-keyboard-check.md` and run it; `.claude/skills/verify` (`/verify`) has the handle for driving the app.

### Pop-ups, menus and sheets

Anything that opens over the page matches the existing ones: frosted glass, the shared scrim and layers, a draggable sheet from the foot on a phone, the same arrive/leave animations, fonts and status colours. Read `docs/agents/ui-overlays.md` before adding or changing one.
