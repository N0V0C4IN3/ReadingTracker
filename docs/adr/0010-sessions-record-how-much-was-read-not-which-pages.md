# ReadingSessions record how much was read, not which pages

A ReadingSession used to record a span — `startPosition` and `endPosition`, "I read from page 98
to page 120" — and a reader's progress was the end of their most recent session.

It asks the reader for something they mostly do not know. People remember that they got through
about thirty pages on the train; they do not remember which page they were on when they opened the
book, and finding out means going and looking. Every reading challenge, every paper reading log and
every other tracker asks for the amount, because the amount is the thing a reader has.

The span model also made progress depend on *when* each session happened, so the most recent one
won. That turned an ordinary mistake — mistyping a date, logging last week's reading today — into a
wrong position, and it made "how much have I read of this?" unanswerable: the answer was always
just wherever the reader last stopped.

## Decision

A ReadingSession records a single `Amount`, in the TrackingMethod it was logged in. Progress is the
**sum** of a LibraryEntry's sessions rather than the last one's end.

- Totals are kept apart by unit and only added together with the effective page count, since pages
  and percentages cannot otherwise be combined. A reader who has logged in both units for a book
  nobody has a page count for is told plainly that the total cannot be worked out, rather than
  given the larger half of it.
- The only amount refused is nothing at all. Reading more than the book has left in it is
  recorded as stated and the *derived* total stops at the whole book — so a reader is never told
  they have read 320 pages of a 300-page book, and never has reading thrown away to prevent it
  either. Reaching the end raises a prompt: "that is all of it — mark it as finished?"
- Correcting a session re-stamps it with the reader's current TrackingMethod. A correction is the
  reader saying what they read, in the unit the history in front of them is written in. Only a
  *change of tracking method* leaves the record alone and converts on the way out, which is the
  invariant that matters: nothing the reader typed is ever silently rewritten.

## Consequences

Progress no longer depends on the order sessions were logged or dated in, so a wrong date is now a
wrong date and nothing more.

**A total that overshoots is a question, not an error.** Someone twenty pages from the end who logs
forty has either misremembered or is holding a longer edition than the one Catalog knows. Refusing
it helps neither, and truncating the session to what "fitted" would destroy the reading for good —
so it is kept as stated, and only the total stops. That matters later: correct the page count
afterwards and the full amount is still there to be counted, because nothing was thrown away.

The cost is that a total no longer distinguishes "finished it" from "read it three times". A
re-read adds sessions to the history, where it is visible, but the total says 100% either way.
Counting completions is a separate question from how far through a book someone is, and it should
be answered by looking at the sessions rather than by letting progress run past the end.

**A reader can no longer record where they stopped.** Someone who wants to note the page they are
on has to work out the amount themselves. That is the trade: the common case gets easier and the
rare one gets harder. If it turns out to matter, the place for it is a bookmark on the LibraryEntry
— "where I am" — which is a different thing from a session and should not be smuggled into one.

**Existing sessions become the distance they spanned** (migration
`RecordSessionsAsAmountRead`). This is not lossless and cannot be: a reader who logged 1→40 and
then 200→260 had a position of 260, but read 99 pages. 99 is what those two sessions actually
record, so 99 is what they become. The alternative would be inventing reading that was never
logged.
