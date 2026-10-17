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
4. Restart KOReader. While reading, **ReadingTracker** is the last entry of the first menu tab (the one
   with contents and bookmarks); in the file manager it is under the tools tab.

## Updating

From 0.3.0 the plugin updates itself over wifi; this install by hand is the last one.

- **Check for updates** in the plugin's menu asks GitHub for the newest plugin release and says
  which version you have. If there is a newer one, you are asked: *Update now*, *Later* or
  *Skip this version*.
- **Once a day**, when the device is already on wifi, it checks by itself and only speaks up when
  there is something new it has not been told to skip. Turn that off with **Check for updates
  once a day on wifi**. Like everything else here, it never turns wifi on.
- **Update now** downloads the release's zip, unpacks it beside the plugin, checks that it is a
  whole plugin, carries your `readingtracker_config.lua` over, and only then swaps the folders.
  If anything fails on the way, the plugin is left as it was and you are told where to get the
  release by hand. Restart KOReader when it asks, and the new version is the one running.
- **Only GitHub's own release is installed.** KOReader does not check certificates, so the
  plugin does it for its update requests: GitHub's certificate is checked against the roots in
  `readingtracker/certs/github.pem` and must be for the host asked, and the zip must be this
  repository's release file, fetched over https all the way. Someone on the same wifi cannot
  pass off a release. If GitHub ever moves to another certificate authority, a check says it
  could not reach GitHub, and installing by hand still works; the next release adds the root.
- A zip holding a link, or a path that would land outside the plugin, is refused whole.
- Unpacking uses KOReader's own archiver. On a KOReader too old to have one, the plugin says so,
  and installing by hand still works.
- To follow a fork's releases, set `update_repo = "owner/name"` in `readingtracker_config.lua`;
  it works whether or not the rest of the config is filled in.

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
- **Reaching the last page** asks, once, whether to mark the book finished — the same question
  the web app asks at 100%. Never asked about a book already finished; a re-read just logs
  sessions.
- The menu shows what the document is linked to, lets you link it to a different book, unlink
  it, sync now, or change its reading status — Want to Read, Reading, On Hold, Dropped or
  Finished — the way the shelf's drop-down does.

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

To release, set `version` in `_meta.lua` and push a matching `plugin-v<version>` tag: CI builds
the zip and attaches it to a GitHub release, which is what the updater looks for. A tag that
does not match `_meta.lua` is refused, so the version a device reports is always the release's.
