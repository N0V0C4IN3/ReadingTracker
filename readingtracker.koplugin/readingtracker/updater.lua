-- Keeping the plugin up to date over wifi: asks GitHub for the repository's releases, picks the
-- newest plugin release, and offers it when it is newer than the one installed. The reader says
-- whether — update now, later, or skip that version — and readingtracker/installer.lua puts it
-- in place, keeping the reader's config file.
--
-- Checked when the reader asks, from the menu, and on its own at most once a day when the
-- device is already online: the plugin never turns wifi on by itself. A check of its own says
-- nothing unless there is something new; one the reader asked for always answers.
--
-- env provides, besides what installer.lua lists:
--   version                     the installed version, read once at start ("0.3.0"), or nil
--   config                      { update_repo = "owner/name" }, optional
--   clock(), is_online(), read_preference(key), save_preference(key, value), notify(text),
--   prompt(prompt, on_answer)   as for the app (readingtracker/app.lua)
--   send(request)               request.trusted = true checks GitHub's certificate
--   working(text)               a notice that stays up while the update downloads; returns done()
--   ask_restart(text)           KOReader's offer to restart

local json = require("readingtracker.vendor.json")
local Installer = require("readingtracker.installer")

local Updater = {}
Updater.__index = Updater

-- Where releases come from unless the config says otherwise (update_repo = "owner/name").
Updater.DEFAULT_REPO = "N0V0C4IN3/ReadingTracker"

-- The file a plugin release carries; the release workflow attaches it under this name.
Updater.ASSET = "readingtracker.koplugin.zip"

-- Preferences, across documents.
Updater.AUTO = "update_check_automatically"   -- false turns the daily check off; on by default
Updater.LAST_CHECKED = "update_last_checked"  -- os.time() of the last answer from GitHub
Updater.LAST_FAILED = "update_last_failed"    -- os.time() of the last check that got none
Updater.SKIPPED = "update_skipped_version"    -- a version the reader said not to offer again

Updater.CHECK_EVERY = 24 * 60 * 60
-- After a check that got no answer — a captive portal, wifi up before DNS — wait this long
-- before the next, so every reconnection does not hold the screen for the timeouts again.
Updater.RETRY_AFTER = 60 * 60

function Updater.new(env)
  local repo = (env.config and env.config.update_repo) or Updater.DEFAULT_REPO
  return setmetatable({ env = env, repo = repo, installer = Installer.new(env, repo) }, Updater)
end

-- "plugin-v0.3.0" or "0.3.0" as { 0, 3, 0 }; nil for anything that is not a version.
function Updater.parse_version(text)
  local digits = type(text) == "string" and (text:match("^plugin%-v(%d[%d%.]*)$") or text:match("^(%d[%d%.]*)$"))
  if not digits or digits:sub(-1) == "." or digits:find("..", 1, true) then
    return nil
  end
  local parts = {}
  for part in digits:gmatch("%d+") do
    table.insert(parts, tonumber(part))
  end
  return parts
end

-- Whether version a is later than b, both as parse_version gives them; missing parts are 0.
function Updater.newer(a, b)
  for index = 1, math.max(#a, #b) do
    local x, y = a[index] or 0, b[index] or 0
    if x ~= y then
      return x > y
    end
  end
  return false
end

-- The newest plugin release in GitHub's list that a reader can install: a plugin-v tag, not a
-- draft or a prerelease, with the plugin's zip attached from this repository's releases.
function Updater.latest(releases, repo)
  local best
  for _, release in ipairs(releases) do
    local version = type(release.tag_name) == "string"
      and release.tag_name:match("^plugin%-v") and Updater.parse_version(release.tag_name)
    local asset
    for _, attached in ipairs(release.assets or {}) do
      if attached.name == Updater.ASSET and Installer.from_release(attached.browser_download_url, repo) then
        asset = attached.browser_download_url
      end
    end
    if version and asset and not release.draft and not release.prerelease
      and (not best or Updater.newer(version, best.parts)) then
      best = { parts = version, version = table.concat(version, "."), url = asset, page = release.html_url }
    end
  end
  return best
end

function Updater:automatic()
  return self.env:read_preference(Updater.AUTO) ~= false
end

function Updater:set_automatic(on)
  self.env:save_preference(Updater.AUTO, on and true or false)
end

-- What asking GitHub found, one of:
--   { kind = "offline" }
--   { kind = "unknown" }                 the installed version could not be read
--   { kind = "failed", problem = text }
--   { kind = "current" }
--   { kind = "available", latest = { version, url, page, parts } }
function Updater:look()
  local env = self.env
  local installed = Updater.parse_version(env.version)
  if not installed then
    return { kind = "unknown" }
  end
  if not env:is_online() then
    return { kind = "offline" }
  end

  local releases, problem = self:releases()
  if not releases then
    env:save_preference(Updater.LAST_FAILED, env:clock())
    return { kind = "failed", problem = problem }
  end
  env:save_preference(Updater.LAST_CHECKED, env:clock())

  local latest = Updater.latest(releases, self.repo)
  if latest and Updater.newer(latest.parts, installed) then
    return { kind = "available", latest = latest }
  end
  return { kind = "current" }
end

-- From the menu: every outcome is said, and a skipped version is offered again, since they asked.
function Updater:check()
  local env = self.env
  if self.installed then
    env:ask_restart("ReadingTracker " .. self.installed .. " is installed. Restart KOReader to use it.")
    return
  end

  local found = self:look()
  if found.kind == "available" then
    self:offer(found.latest)
  elseif found.kind == "current" then
    env:notify("ReadingTracker " .. env.version .. " is the latest version")
  elseif found.kind == "offline" then
    env:notify("Connect to wifi to check for updates")
  elseif found.kind == "failed" then
    env:notify(found.problem)
  else
    env:notify("This copy of ReadingTracker does not say its version, so it cannot update itself."
      .. " Install the latest release by hand from https://github.com/" .. self.repo .. "/releases")
  end
end

-- When the device comes online: at most once a day, an hour after a check that got no answer,
-- and quiet unless there is a version the reader has not already turned down.
function Updater:onNetworkConnected()
  local env = self.env
  if not self:automatic() or self.installed then
    return
  end
  local now = env:clock()
  local checked = tonumber(env:read_preference(Updater.LAST_CHECKED))
  local failed = tonumber(env:read_preference(Updater.LAST_FAILED))
  if (checked and now - checked < Updater.CHECK_EVERY) or (failed and now - failed < Updater.RETRY_AFTER) then
    return
  end

  local found = self:look()
  if found.kind == "available" and env:read_preference(Updater.SKIPPED) ~= found.latest.version then
    self:offer(found.latest)
  end
end

function Updater:offer(latest)
  local env = self.env
  env:prompt({
    text = "ReadingTracker " .. latest.version .. " is available — you have " .. env.version .. ". Update now?",
    choices = {
      { id = "update", label = "Update now" },
      { id = "later", label = "Later" },
      { id = "skip", label = "Skip this version" },
    },
  }, function(choice)
    if choice == "skip" then
      env:save_preference(Updater.SKIPPED, latest.version)
    elseif choice == "update" then
      self:install(latest)
    end
  end)
end

function Updater:install(latest)
  local env = self.env
  local done = env:working("Downloading ReadingTracker " .. latest.version .. "…")
  local ok, reason, stranded = self.installer:install(latest.url)
  done()

  if ok then
    self.installed = latest.version
    env:ask_restart("ReadingTracker " .. latest.version .. " is installed. Restart KOReader to use it.")
  elseif stranded then
    env:notify("The update could not be installed (" .. tostring(reason) .. "), and the plugin could not"
      .. " be put back. It is at " .. stranded .. ": rename it to readingtracker.koplugin, or install "
      .. latest.version .. " by hand from " .. tostring(latest.page))
  else
    env:notify("The update could not be installed (" .. tostring(reason)
      .. "). The plugin is as it was; it can be installed by hand from " .. tostring(latest.page))
  end
end

-- GitHub's list of releases, or nil and what to tell the reader.
function Updater:releases()
  local env = self.env
  local status, text = env:send({
    method = "GET",
    url = "https://api.github.com/repos/" .. self.repo .. "/releases?per_page=100",
    trusted = true,
    headers = {
      ["Accept"] = "application/vnd.github+json",
      -- GitHub refuses a request that does not say what is making it.
      ["User-Agent"] = "ReadingTracker-KOReader/" .. env.version,
    },
  })

  if status == nil then
    -- "offline" is the usual reason; a certificate that is not GitHub's is worth saying.
    local why = text and text ~= "offline" and (" (" .. tostring(text) .. ")") or ""
    return nil, "GitHub could not be reached just now" .. why
  end
  if status ~= 200 then
    return nil, "GitHub did not answer the update check (" .. tostring(status) .. ")"
  end

  local ok, decoded = pcall(json.decode, text or "")
  if not ok or type(decoded) ~= "table" then
    return nil, "GitHub's answer to the update check could not be read"
  end
  return decoded
end

return Updater
