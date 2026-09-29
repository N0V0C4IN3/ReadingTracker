# Phone keyboard check

Run this on any change that touches a text field on a phone: the shelf's search dock, Settings' edition search, a form in a sheet, or anything that decides what a phone's screen is (`Orientation`, `readingTracker.isSideways` in `index.html`, the `search`, `sheet` and `preview` rules in `wwwroot/css/app.css`, `js/sheet.js`).

A phone's keyboard is only raised by a `focus()` made inside the tap that asked for it, and once up it must stay up. A change can break either without failing a build or a test, so check it in the browser.

## Why it is its own check

On Android the keyboard shortens the window (`interactive-widget=resizes-content` in `index.html`), so a portrait phone with the keyboard up is a short, wide window. Anything that reads the window's shape (a media query, `innerWidth > innerHeight`) then sees a phone on its side. Once, that turned the shelf into the cover flow the moment the search box was tapped: the dock went, focus fell to the page, and the keyboard closed with it. The window is not the phone; use `screen.orientation` for the device.

## What to check

1. **Opens with the cursor in the box.** At phone width (390×844, touch), tap the `+`. The dock opens and `document.activeElement` is the `Find a book` input.
2. **Survives a keyboard-sized window.** With the dock open and the input focused, shrink the window to about 390×340, shorter than it is wide. The dock is still there (`.search--open`, `display: flex`), focus is still on the input, and the shelf is still the list (no `.flow--shelf`).
3. **Still turns on its side.** At 844×390 with touch and landscape, the shelf is the cover flow; back at 390×844 it is the list again.
4. **Every other text field the change touches**, the same way: open it, shrink the window, and confirm the field, its sheet and focus survive.

## What it cannot show

An emulated window cannot raise a real keyboard, so this catches the layout and focus half of the bug and not a `focus()` a phone refuses. Say so in the review when the change moves focus code (`armOpener`, `FocusAsync`, `focusOpener`), and ask for it to be tried on a phone.

A browser with no `screen.orientation` falls back to the window's shape, which is the old bug. To see that path, pass `Object.defineProperty(window.screen, 'orientation', { value: undefined, configurable: true })` in the `initScript` and repeat step 2: the cover flow comes back. It is only old iOS, which does not shrink the window for the keyboard anyway.

## Running it

The launch, the phone emulation and the sign-in script are in `.claude/skills/verify/SKILL.md`, under Handle.
