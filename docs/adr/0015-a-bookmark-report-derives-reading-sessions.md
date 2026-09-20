# A Bookmark report derives ReadingSessions on the server

A ReadingSession records how much was read, never where the reader is (ADR-0010). An e-reader
knows the opposite: it knows the page it is showing and nothing about how much of that was read
today. For a device to feed the tracker, someone has to turn a stream of positions into sessions,
and ADR-0010 had already named the place a position would live if one were ever needed — "a
bookmark on the LibraryEntry".

## Decision

The LibraryEntry carries a **Bookmark**: where the reader is, as a percentage, as last reported
by a device, with the time of the reading. A device only ever says "I am at 34%", through
`PUT /api/library/{entry}/bookmark`, and Library works out what that means:

- Forward of the Bookmark, the difference is a ReadingSession in percent, stamped as coming from
  a `Device` and dated when the reading happened rather than when the report got through.
- Backward, or no further, is not reading and records nothing; the Bookmark moves anyway.
- The first report, when there is no Bookmark yet, is measured against the reader's Amount read
  as a percentage, so hand-logged reading is neither counted twice nor thrown away. Pages logged
  against a book of unknown length cannot be expressed as a percentage, so that baseline is zero.
- A `Want to Read`, `On Hold` or `Dropped` book moves to `Reading`; a `Finished` one stays
  `Finished`, because a re-read is sessions and not a status (ADR-0010).

The Bookmark is a report of a position, never a source of truth for Amount read. Correcting or
deleting sessions leaves it where the device put it; it feeds the total only through the sessions
it makes.

## Considered Options

**The device keeps the last synced position and posts sessions for the difference.** Simpler
server, and the server's model stays untouched — but the truth then lives on the device. A wiped
Kindle, a second device, or a session corrected in the web app leaves the device's idea of "last
synced" wrong with nothing to correct it, and every device has to get the arithmetic right for
itself.

**Sessions in pages, mapped from the device's percentage through the edition's page count**, as
the Hardcover plugin does. Rejected because editions differ and a reader's Kindle rendering of an
EPUB has its own page numbers that change with the font; the percentage is the one measurement
the device actually has, and the Library already converts between units on the way out.

**Merging consecutive device reports into one session** — a report within half an hour of the
last extending it rather than adding to it — was considered and deferred. One report is one
session for now; the plugin only ever says where it is, so merging can be added later without
touching it. The history will look like a polling interval until then, which is accepted.

## Consequences

- Every ReadingSession now says whether the reader typed it or a device reported it (`Source`),
  and the web app can show as much. Correcting a device's session keeps it a device's session.
- The server needs the effective page count *before* it can take a first report, since the
  baseline is a percentage, so the endpoint goes to Catalog first rather than alongside — and a
  first report is refused (503) while Catalog cannot be reached, because reading "could not say"
  as "no length" would measure hand-logged pages from zero and count them twice, for good. Later
  reports are percent against percent and take Catalog's absence in their stride.
- The Bookmark is moved only from where it was read, in the same transaction as the session it
  makes, so two reports racing for one book — two devices, or one retrying a request that had not
  failed — credit the difference once; the loser is told to report again (409).
- A device report on a book that was `On Hold` or `Dropped` un-parks it without asking. That is
  the trade: a reader who opens a dropped book on their Kindle to check something will find it
  back under `Reading`, and has the web app to put it back.
- The Bookmark is read-only in the web app. Setting it by hand would make it a second source of
  truth for progress, which is exactly what it is not.
