# What the web frontend already has

Most review findings on `ReadingTracker.Web` PRs are a second copy of something that already exists: status colours copied a fourth time, `ReaderDays.Of` written again inline, a second cover flow beside `CoverFlow.razor`. Before writing a helper, a rule, a JS module or a style, find its row here and use the existing piece. If you add a new shared piece, add its row.

## Words and numbers (`Services/`)

| Job | Use |
| --- | --- |
| The day a session belongs to (the reader's zone, not UTC) | `ReaderDays.Of` |
| A status's words, the full list, what a book can be added as, "wanted and not begun" | `ReadingStatuses` (`Label`, `All`, `AddableAs`, `NotBegun`) |
| Shelf order: grouping by status, finished books newest first | `ShelfOrder` (`ByStatus`, `Rank`) |
| How much of a book is read (pages, percent, nothing, unknown length) | `AmountRead`. Library works it out; a display only reads it |
| Numbers as text: "12.5", "1 page", "under a page", "3 weeks ago", durations | `Figures` |
| Authors and imprint as a byline, the letter on a cover with no picture | `BookText` |
| A cover URL inside a `style` attribute | `CoverCss.Url` |
| Sessions grouped by day, one day's pages, a year of reading | `SessionDays`, `DailyPages`, `ReadingYear` |
| Goal pace, reading pace | `GoalPace`, `ReadingPace` |
| "The shelf changed", for the header's goal badge | `ShelfChanges`. `GatewayLibraryClient` already raises it after a status change or a removal |
| Where the reader was on the shelf, for coming back from another page | `ShelfMemory`, `ShelfPlaceKeeper` |
| A phone held on its side | `Orientation` (scoped; watches the phone turning) |

## Components

| Job | Use |
| --- | --- |
| Jackets at an angle, steerable | `FlowStage`. `CoverFlow` (welcome) and `ShelfDeck` (sideways shelf) both wrap it |
| A status picker | `StatusSelect` |
| A tracking-method picker | `TrackingSelect` |
| Progress bar, pace stripe, pages chart | `ProgressBar`, `PaceStripe`, `PagesChart` |
| The busy placeholder | `Settling` |
| Asking before removing a book, prompting to finish one | `RemovePrompt`, `FinishPrompt` |

## Browser side (`wwwroot/`)

| Job | Use |
| --- | --- |
| Anything leaving or arriving with an animation | `js/motion.js`: add the selector to `LEAVES`; the stylesheet plays `.is-leaving` / `.is-entering` |
| A phone bottom sheet dragged between heights | `js/sheet.js` |
| A sheet kept above the phone keyboard | `js/liftSheet.js` |
| Reduced motion, dark theme, view transitions, waiting for an element or for covers | `window.readingTracker` in `index.html` (`prefersStillness`, `isDark`/`setDark`, `transition`, `arrival`, `coversSettled`) |
| Swiping between the shelf's status tabs | `js/shelfSwipe.js` |
| Keeping and restoring the reader's scroll on the shelf and in the search dock | `js/shelfPlace.js`, driven by `ShelfPlaceKeeper` |
| Colours taken from a book's jacket (the Book page) | `js/coverPalette.js` |
| The stop slider's floor (it can't go behind where the reader was) | `js/stopSlider.js` |
| The reading calendar scrolled to the latest weeks on a phone | `js/calendar.js` |
| The raw Google ID token | `js/idToken.js`, through `IdTokenProvider` |
| Is this a phone | There are two phone widths today: `max-width: 40rem` for the shelf page (its tabs, the search dock, and `shelfSwipe.js`, which asks with the same query), and `max-width: 560px` for cards, sheets and panels. Put a rule in the block its feature already uses, and don't add a third. The Devices page (`480px`) and the reading stats (`900px`) have their own layout breaks. For a phone on its side, use `Orientation` |

## Styles (`wwwroot/css/app.css`)

- **Status colours.** Every status dot reads `--dot`, and there are two schemes for setting it, both in one place in `app.css`:
  - **A list of statuses** (`.status__row--*`, `.preview__status--*`, `.import__tone--*`) uses the statuses' own tokens: `--status-alive` (want to read, reading), `--status-done` (finished), `--ink-faint` (on hold, dropped).
  - **A card's own status** (`.status__trigger--*`) uses the card's `--accent`, which a jacket can repaint, plus `--warn` and `--ink-faint`.

  Add a new list's class to the first group's selectors (and to the ring rule under it). Don't write the colours again.
- **Pop-ups, menus and sheets.** See `ui-overlays.md`.
- **Size.** `app.css` is past the 1000-line budget. `tools/check-web.sh` lets a branch grow it by 50 lines at most. A feature that needs more gets a stylesheet of its own in `wwwroot/css/`, linked from `index.html` after `app.css` and using the same tokens. `Home.razor` is held the same way: move code out into a component.
- **Dropped patterns.** These live in `tools/check-web.sh`, which fails a commit that brings one back. When you replace a pattern for good, add it there.
