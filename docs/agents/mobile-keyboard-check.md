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

An emulated window cannot raise a real keyboard, so this catches the layout and focus half of the bug and not a `focus()` a phone refuses. Say so in the review when the change moves focus code (`armOpener`, `FocusAsync`, `focusOpener`), and ask for it to be tried on a phone.

## Running it

Chrome DevTools (`emulate` with `<w>x<h>x3,mobile,touch[,landscape]`, then `evaluate_script`). Local dev sign-in may be off in `appsettings.Development.json`; do not edit the file to test. Pass this as `initScript` on the navigation instead, and it signs in as the dev reader for that page only:

```js
const of = window.fetch.bind(window);
window.fetch = async (input, init) => {
  const url = typeof input === 'string' ? input : (input.url || String(input));
  const r = await of(input, init);
  if (!url.includes('appsettings.Development.json')) return r;
  const t = await r.text();
  return new Response(t.replace(/"Enabled"\s*:\s*false/, '"Enabled": true'),
    { status: 200, headers: { 'content-type': 'application/json' } });
};
```

`http://localhost:5200` is the local stack (`docker compose --profile services up --build -d`).
