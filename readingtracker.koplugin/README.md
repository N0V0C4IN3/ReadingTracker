# ReadingTracker for KOReader

Links the book you open on your e-reader to your ReadingTracker shelf, and reports where you
are as you read. Works with a **device token** rather than a Google sign-in, since an e-reader
cannot do one of those.

## Install

1. In the ReadingTracker web app, open the account menu → **Devices**, name your device and
   create a token. Copy it — it is shown once.
2. Download `readingtracker.koplugin.zip` from the latest release and unzip it into KOReader's
   `plugins/` folder, so you have `plugins/readingtracker.koplugin/main.lua`.
3. In that folder, copy `readingtracker_config.example.lua` to `readingtracker_config.lua` and
   fill in your Gateway's address and the token.
4. Restart KOReader. The plugin sits under the tools menu as **ReadingTracker**.

## What it does

- **Opening a book with an ISBN** in its metadata looks the book up. If it is already on your
  shelf it is linked silently; if not, you are asked once whether to add it — *Yes*, *Not now*
  (asked again next time), or *Never for this document*.
- **A book with no ISBN**, or one the library does not know, asks once whether to search: the
  title and author are filled in from the file, you pick the right result, and it is added to
  your shelf and linked. Nothing found? Add it by hand from the same place.
- **While you read**, where you are is reported as a percentage of the document — when you
  close the book, when the device sleeps, and every five minutes while paging (the interval is
  in the menu). ReadingTracker turns each report into a reading session and shows the position
  on your shelf card. A report that cannot be sent is kept and sent at the next chance, with the
  time you actually read.
- The menu shows what the document is linked to, lets you link it to a different book, unlink
  it, or sync now.

Nothing is sent while the device is offline; the plugin tries again at the next chance and
never turns wifi on by itself.

## Developing

Everything the plugin decides lives in `readingtracker/app.lua`, driven through the handlers
KOReader calls, against an `env` that `main.lua` builds from KOReader and the specs fake. The
specs run on LuaJIT — what KOReader runs — via Docker, without installing Lua:

```sh
spec/run.sh
```

CI runs the same suite and builds the release zip.
