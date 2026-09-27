# Pop-ups, menus and sheets

Every surface that opens over the page (a menu, a sheet, a panel, a dialog) is the same kind of thing and looks and moves like the others. Before adding one, copy an existing one rather than inventing a style:

| Kind | Reference in `wwwroot/css/app.css` |
| --- | --- |
| Dropdown menu | `.status__menu`, `.account__menu` |
| Bottom sheet | `.sheet` (log reading, history, settings), `.search` (the phone dock) |
| Side sheet | `.preview` (a search result opened) |

All the values below are tokens that already exist on `:root`. Never hard-code a colour, blur, radius, shadow or curve that a token already names. Keep both themes working: every token has a dark value.

## Surface

- **Glass.** Set `background-color: var(--card-frosted)` with `backdrop-filter: var(--blur-soft)` (and `-webkit-backdrop-filter`) inside `@supports ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px)))`. Outside it, fall back to `var(--card-raised)`. The page behind should show through as soft shapes, not as flat paint.
- **Edge.** Use `1px solid var(--line-strong)` on the side facing the page, with `box-shadow: var(--shadow-lifted)`.
- **Corners.**
  - Menus: `var(--radius-sm)`.
  - Phone bottom sheets: `22px 22px 0 0` (the dock and the result sheet). The older sheets use `18px`.
  - Menu rows: `8px`.

## Scrim and layering

- Put a `<button type="button" class="…__scrim" tabindex="-1" aria-hidden="true">` behind the surface. A tap on it closes the surface.
- The scrim is `rgb(0 0 0 / 40%)` on a phone. Behind a desktop dropdown it's transparent.
- Layers:
  - The header is 20.
  - A sheet's scrim is 21, the sheet 22.
  - The search dock and its `+` button are 10.
- **Nothing behind scrolls.** Add `html:has(<surface>:not(.is-leaving)) { overflow: hidden; }` and `touch-action: none` on the scrim. Give the surface's own scroll area `overscroll-behavior: contain`.
- Hide the phone's `+` button while a sheet is up: `body:has(<surface>) .search__fab { visibility: hidden; }`.

## Phone: a sheet from the foot

- **Breakpoint.** At `@media (max-width: 560px)` a surface becomes a bottom sheet, pinned `left/right/bottom: 0`, with `env(safe-area-inset-bottom)` in its bottom padding.
- **Resizable.** A sheet that holds more than a short list is resizable like the search dock:
  - Put a `<button type="button" class="search__grip" aria-label="…">` first inside it.
  - Call `js/sheet.js`: `attach(el, "default")` after the first render, and `detach(el, true)` on dispose.
  - Set `--sheet-peek` to what should stay visible when peeked. Add `height: 55dvh` (where `attach` opens it) and `transition: height 260ms var(--ease-out)`.
  - Hide what a peek would cut off, for example `[data-sheet="peek"] … { visibility: hidden; }`.
- **Grip.** A short list, like the status menu, stays a fixed sheet with a grip pill drawn by `::before`: `2.25rem × 4px`, `var(--line-strong)`.

## Motion

- **Arriving.**
  - Menus and panels: `unfold` (240ms, `var(--ease-out)`).
  - Phone sheets: `dock` (260ms).
  - Scrims: `fade` (200ms).
  - Side sheets: a short slide in from their edge.
- **Leaving.** Add the surface and its scrim to `LEAVES` in `js/motion.js`, and give each an `.is-leaving` rule:
  - Menus: `lift`.
  - Phone sheets: `undock` (220ms, `cubic-bezier(0.4, 0, 1, 1)`).
  - Scrims: `clear`.
  - Side sheets: back out to their edge.

  Without this, Blazor removes the surface in one frame.
- **Curves.** Use `var(--ease-out)` for anything arriving or coming to rest, and `var(--ease)` for state changes under the pointer. `var(--quick)` (160ms) is for hover and fill changes.
- **Reduced motion.** `prefers-reduced-motion` is handled globally. Don't add motion that bypasses it.

## Type and colour

- Use the system sans (`system-ui, -apple-system, "Segoe UI", sans-serif`) for everything a reader operates. Georgia is only for prose, such as a book's description (`.book__about`).
- Ink:
  - `var(--ink)` for content.
  - `var(--ink-soft)` for secondary text.
  - `var(--ink-faint)` for tertiary text and labels.
- The one action is `.button--primary` (`var(--accent)` / `var(--accent-ink)`). The rest are the default soft-fill button, or `.link-button`.
- **Menu rows.**
  - Base: `.status__row` — transparent, `var(--fill)` on hover and focus, `0.92rem`, weight 500.
  - The current row is tinted in its own colour.
- **Statuses** always carry their dot (`.status__dot`, colours from `.status`):
  - Reading: solid `--status-alive`.
  - Want to read: `--status-alive` as a ring.
  - Finished: solid `--status-done`.
  - On hold: solid `--ink-faint`.
  - Dropped: `--ink-faint` as a ring.

  A selected status is washed in its colour: `color-mix(in srgb, var(--dot) 16%, transparent)`, ringed at 55%.

## Behaviour

- **Dialog semantics.** A sheet is `role="dialog" aria-modal="true"`, labelled by its heading. Focus goes to its Close button when it opens, and Escape closes it.
- **Close control.** A round `2.75rem` button with the `×` path used across the app, labelled `aria-label="Close"`. On a phone it sits at the right end of the grip strip.
- **Touch targets.** At least 44px on a phone.
