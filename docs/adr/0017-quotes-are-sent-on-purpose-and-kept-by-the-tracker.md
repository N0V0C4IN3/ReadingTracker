# Quotes are sent on purpose and kept by the tracker

A Reader highlighting on their e-reader wants to keep some of those passages with the Book in
ReadingTracker. KOReader already keeps every highlight in the document's own settings, along with
the note the Reader wrote on it, where it is, and how it is drawn. The plugin could have treated
that list as something to mirror. But most highlights are not passages to keep: a word to look up,
a place to come back to, a thumb that slipped.

## Decision

A **Quote** reaches ReadingTracker only when the Reader sends it. KOReader's highlight pop-up gets
*Save to ReadingTracker*, and nothing else about a highlight is ever sent. The send goes through the
plugin's existing queue for when it is offline, like a Bookmark report. A document that is not linked
to a shelf book runs the linking flow first, and if the Reader backs out of that, nothing is sent.

Once sent, the Quote is the tracker's. Deleting the highlight on the device, or restyling it, leaves
the Quote alone. In the web app the Reader can delete a Quote or edit its note. The passage is the
Book's words and is read-only.

Each send carries the highlight's identity on the device, stable for as long as that highlight
exists, and Library treats a second send with the same identity as the same Quote. Retrying a
request that had already gone through can never make a duplicate. Sending again on purpose updates
the note: this is the one way an edit made on the device reaches the tracker, and the plugin tells
the Reader "Updated" rather than "Saved". A different highlight of the same words, a re-read's say,
is a new Quote.

Quotes live in Library, one LibraryEntry's own, and go when it goes.

## Considered Options

**Syncing every highlight automatically.** No extra tap, and the tracker would always agree with
the device. Rejected because the tracker would fill with highlights that were never meant to be
kept. It would also have to decide what a deletion on the device means for something the Reader may
have noted on in the web app, and a wiped device or a second one would read as the Reader deleting
everything.

**Importing `My Clippings.txt`** from the Kindle's own reader, or KOReader's export, in batches.
Left for later rather than rejected: it is a different device's way in, and it can land on the same
Quote, with the same rule about sending twice, if it is ever wanted.

**Matching resends by the passage's text** instead of the highlight's identity. Simpler for the
plugin, but it would merge a passage saved again on a re-read into the first Quote, and it would
break the moment a hyphenation or whitespace difference crept in between two sends.

## Consequences

- The tracker can hold Quotes whose highlight no longer exists anywhere, and that is intended. It
  also means a Quote deleted in the web app cannot come back unless its highlight is still on the
  device to send again.
- The plugin has to remember which highlights it has sent, in the document's settings, so it knows
  whether to say "Saved" or "Updated".
- A Reader who edits a note in both places keeps whichever was saved last. There is no merging.
