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
| Another site's export coming in (Hardcover, Goodreads, StoryGraph) | `LibraryExport.Parse` |
| The shelf going out: whole or not at all, then as Goodreads' CSV | `ShelfFile.Of` (file, empty, not loaded, signed out); `GoodreadsCsv.Write`, whose columns and status-to-shelf table the importer's Goodreads entry reads too |
| CSV cells, quoted and unquoted, either way | `Csv` (`Read`, `Write`) |
| An ISBN's shape, and its ten- and thirteen-digit forms | `Isbn` (`Normalise`, `TenOf`, `ThirteenOf`) |
| The day a book was finished, while it still is | `ShelfOrder.FinishedOn` |
| Saying which day a book was finished when it becomes Finished: the Reader's today, a stated day, or unknown | `FinishedDay`, passed to `GatewayLibraryClient.SetStatusAsync` / `AddAsAsync`. Leave it out and the client says the Reader's today itself |
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
| Exporting the shelf as a file (the Import page's foot) | `ShelfExport` |

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
| Saving text the page made as a file in the reader's downloads (the shelf's CSV export) | `js/download.js` (`save(fileName, text, type)`) |
| The raw Google ID token | `js/idToken.js`, through `IdTokenProvider` |
| Recording why a sign-in callback failed | `js/signInDiagnostics.js` |
| Is this a phone | Two phone widths, by what they hold today. `max-width: 40rem` is the page frame and the shelf: `main`, the header and goal badge, the shelf cards (`.entry`), the Book page's head, form fields and the log form, the filters and the search dock (and `shelfSwipe.js` asks the same query). `max-width: 560px` is what opens over the page and the other pages: the preview and other sheets, the status menu, settings, the pace stripe, stats and calendar, import. Put a rule in the block its neighbours are in, and don't add a third. `900px` is a layout break for stats and import; `480px` is the Devices page; `min-width: 49rem` places the Book page's back arrow in the margin. For a phone on its side, use `Orientation` |

## Styles (`wwwroot/css/app.css`)

- **Status colours.** Every status dot reads `--dot`, and there are two schemes for setting it, both in one place in `app.css`:
  - **A list of statuses** (`.status__row--*`, `.preview__status--*`, `.import__tone--*`) uses the statuses' own tokens: `--status-alive` (want to read, reading), `--status-done` (finished), `--ink-faint` (on hold, dropped).
  - **A card's own status** (`.status__trigger--*`) uses the card's `--accent`, which a jacket can repaint, plus `--warn` and `--ink-faint`.

  Add a new list's class to the first group's selectors (and to the ring rule under it). Don't write the colours again.
- **Pop-ups, menus and sheets.** See `ui-overlays.md`.
- **Size.** `tools/check-web.sh` holds every file to 1000 lines, and lets a branch grow one already past that (today `app.css` and `Home.razor`) by 50 lines at most. When a stylesheet is full, a feature gets one of its own in `wwwroot/css/`, linked from `index.html` after `app.css` and using the same tokens. When a page is full, move code out into a component.
- **Dropped patterns.** These live in `tools/check-web.sh`, which fails a commit that brings one back. When you replace a pattern for good, add it there.
