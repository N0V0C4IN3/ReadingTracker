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
| Is this a phone | In CSS, the existing `@media (max-width: 560px)` blocks. Put new phone rules there rather than adding a breakpoint. Use `Orientation` for sideways. Don't ask from JS when CSS can decide |

## Styles (`wwwroot/css/app.css`)

- **Status colours.** The tokens are `--status-alive` (in hand) and `--status-done` (finished), plus `--accent`, `--warn` and `--ink-faint`, which feed `--dot` on `.status__trigger--*`, `.status__row--*` and `.preview__status--*`. Add your class to those selectors; don't repeat the colours.
- **Pop-ups, menus and sheets.** See `ui-overlays.md`.
- **Dropped patterns.** These live in `tools/check-web.sh`, which fails a commit that brings one back. When you replace a pattern for good, add it there.
