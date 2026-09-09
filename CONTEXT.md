# ReadingTracker

A web application for tracking personal reading progress across books — what you're reading, how far you've gotten, and your history of reading activity.

## Language

**Book**:
Catalog metadata for a title — author(s), ISBN, cover image, total page count when known — sourced from an external book-data provider, or entered by hand when no provider match exists. A Book is global/shared, not owned by any one Reader, and is cached locally the first time it's referenced rather than re-fetched live on every use.
_Avoid_: Title (ambiguous with the `title` field itself), Edition

**Catalog**:
The shared body of Book metadata the whole system draws on — the answer to "what books exist, and what do we know about them" — independent of any particular Reader. Sourced from external book-data providers, or entered by hand when no provider has a match.
_Avoid_: Library (that is one Reader's own collection, which is a different thing)

**Reader**:
A person who uses ReadingTracker: the owner of a Library, of LibraryEntries and of ReadingSessions. Identified by whoever signed in, never by anything the client claims about itself. One Reader's shelf is invisible to every other Reader, and the Books they read are shared while their relationship to those Books is not.
_Avoid_: User (this was the earlier name for exactly this concept; the two were never different things, and everything now says Reader), Account, Profile

**LibraryEntry**:
The association between a Reader and a Book: "this book is in this Reader's library." Holds the current ReadingStatus and the Reader's chosen TrackingMethod for this book. Distinct from the Book itself (shared catalog data) and from a ReadingSession (a record of activity).
_Avoid_: UserBook, Entry (too generic on its own)

**TrackingMethod**:
How a LibraryEntry's progress is expressed: `Pages` or `Percentage`, chosen per-LibraryEntry and changeable at any time. Switching method is a display-time conversion (using the effective page count, which the Reader may override), not a rewrite of past ReadingSessions — each ReadingSession keeps the unit it was actually logged in.
_Avoid_: ProgressUnit

**Effective page count**:
How long a book is *for one reader*: their own page count for the edition in their hands where they have set one, and otherwise the Book's `totalPages` from the Catalog. It is what every percentage and every TrackingMethod conversion is worked out from, and it may still be unknown — a book nobody has a page count for can be tracked, just not as a percentage. A reader's own count belongs to their LibraryEntry and never changes the shared Book.
_Avoid_: Page count (ambiguous — say whose), Total pages (that is the Book's field specifically)

**ReadingSession**:
A discrete, timestamped stretch of reading activity against one LibraryEntry — e.g. "read 22 pages on Tuesday evening." The record of *when and how much* someone read: an amount, in whichever TrackingMethod was active when it was logged, and never the pages it spanned (ADR-0010). It is kept exactly as the Reader stated it, including when that is more than the Book has left in it. A LibraryEntry's full reading history is its sequence of ReadingSessions, and re-reading produces new ReadingSessions rather than a new ReadingStatus or a new Book record.
_Avoid_: Log, reading log, entry (these are ambiguous with application/infrastructure logging, which is a separate, non-domain concern), Position (a session says how much, not where)

**Amount read**:
How much of a Book a Reader has got through: the sum of their ReadingSessions for that LibraryEntry, worked out on every read rather than stored, so it can never disagree with the history it comes from. Expressed in the LibraryEntry's TrackingMethod where the effective page count allows the conversion, and otherwise in the unit the Reader actually logged. It never exceeds the Book's length — a Reader cannot have read more of a Book than there is of it — so logging past the end tops out at the whole book and is a good moment to ask whether they have finished. Unknown length means nothing to stop at, and it runs on.
_Avoid_: Progress (fine in prose, but ambiguous between this and the percentage shown beside it), Position, Current page

**ReadingStatus**:
The current state of a LibraryEntry: `Want to Read`, `Reading`, `Finished`, `On Hold`, or `Dropped`. A LibraryEntry has exactly one current ReadingStatus at a time; it does not itself carry history — history lives in ReadingSessions.
_Avoid_: State (too generic)
