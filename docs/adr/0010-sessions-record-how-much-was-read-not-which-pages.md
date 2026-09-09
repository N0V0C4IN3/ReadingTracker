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
- Validation checks the single session, not the running total: an amount must be more than
  nothing, and cannot be longer than the whole book in one sitting. The old
  `RunsPastTheEndOfTheBook` check on the position is gone, because a total legitimately runs past
  the end — see re-reading below.
- Correcting a session re-stamps it with the reader's current TrackingMethod. A correction is the
  reader saying what they read, in the unit the history in front of them is written in. Only a
  *change of tracking method* leaves the record alone and converts on the way out, which is the
  invariant that matters: nothing the reader typed is ever silently rewritten.

## Consequences

Progress no longer depends on the order sessions were logged or dated in, so a wrong date is now a
wrong date and nothing more.

**Re-reading works, and is visible.** Going through a 300-page book twice is 600 pages read, and
the shelf says so. The progress bar stops at 100% rather than running off the end of itself, so the
number and the bar say different true things: the bar is "how much of it is behind you", the number
is "how much you have read".

**A reader can no longer record where they stopped.** Someone who wants to note the page they are
on has to work out the amount themselves. That is the trade: the common case gets easier and the
rare one gets harder. If it turns out to matter, the place for it is a bookmark on the LibraryEntry
— "where I am" — which is a different thing from a session and should not be smuggled into one.

**Existing sessions become the distance they spanned** (migration
`RecordSessionsAsAmountRead`). This is not lossless and cannot be: a reader who logged 1→40 and
then 200→260 had a position of 260, but read 99 pages. 99 is what those two sessions actually
record, so 99 is what they become. The alternative would be inventing reading that was never
logged.
