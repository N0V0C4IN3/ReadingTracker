local _ = require("gettext")
return {
  name = "readingtracker",
  fullname = _("ReadingTracker"),
  description = _([[Links the book you are reading to your ReadingTracker shelf and reports where you are as you read.]]),
  -- The release's version: the release workflow refuses a plugin-v tag that does not match it,
  -- and the plugin's updater compares it with the newest release on GitHub.
  version = "0.3.2",
}
