-- Copy this file to readingtracker_config.lua, in the same folder, and fill in both values.
-- readingtracker_config.lua is yours alone: it holds a secret and is never part of a release.
return {
  -- Where your ReadingTracker Gateway answers, with the scheme and a trailing slash.
  base_url = "https://your-gateway.example.com/",

  -- A device token from the Devices page in the web app. Shown once, at minting; if you lose
  -- it, revoke it there and mint another.
  token = "rt_...",

  -- Optional: where the plugin looks for its own updates, as "owner/repository" on GitHub.
  -- update_repo = "N0V0C4IN3/ReadingTracker",
}
