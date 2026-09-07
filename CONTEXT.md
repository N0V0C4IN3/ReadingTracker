# ReadingTracker

A web application for tracking personal reading progress across books — what you're reading, how far you've gotten, and your history of reading activity.

## Language

**Book**:
Catalog metadata for a title — author(s), ISBN, cover image, total page count when known — sourced from an external book-data provider, or entered by hand when no provider match exists. A Book is global/shared, not owned by any one user, and is cached locally the first time it's referenced rather than re-fetched live on every use.
_Avoid_: Title (ambiguous with the `title` field itself), Edition

**Catalog**:
The shared body of Book metadata the whole system draws on — the answer to "what books exist, and what do we know about them" — independent of any particular user. Sourced from external book-data providers, or entered by hand when no provider has a match.
_Avoid_: Library (that is one user's own collection, which is a different thing)

**LibraryEntry**:
The association between a User and a Book: "this book is in this user's library." Holds the current ReadingStatus and the user's chosen TrackingMethod for this book. Distinct from the Book itself (shared catalog data) and from a ReadingSession (a record of activity).
_Avoid_: UserBook, Entry (too generic on its own)

**TrackingMethod**:
How a LibraryEntry's progress is expressed: `Pages` or `Percentage`, chosen per-LibraryEntry and changeable at any time. Switching method is a display-time conversion (using the Book's `totalPages`, which the user may override), not a rewrite of past ReadingSessions — each ReadingSession keeps the unit it was actually logged in.
_Avoid_: ProgressUnit

**ReadingSession**:
A discrete, timestamped stretch of reading activity against one LibraryEntry — e.g. "read from page 40 to 62 on Tuesday evening." The record of *when and how much* someone read, in whichever TrackingMethod was active when it was logged. A LibraryEntry's full reading history is its sequence of ReadingSessions. Re-reading a book produces new ReadingSessions rather than a new ReadingStatus or a new Book record.
_Avoid_: Log, reading log, entry (these are ambiguous with application/infrastructure logging, which is a separate, non-domain concern)

**ReadingStatus**:
The current state of a LibraryEntry: `Want to Read`, `Reading`, `Finished`, `On Hold`, or `Dropped`. A LibraryEntry has exactly one current ReadingStatus at a time; it does not itself carry history — history lives in ReadingSessions.
_Avoid_: State (too generic)
