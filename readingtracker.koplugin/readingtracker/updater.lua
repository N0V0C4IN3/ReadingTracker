-- Keeping the plugin up to date over wifi: asks GitHub for the repository's releases, picks the
-- newest plugin release, and offers it when it is newer than the one installed. The reader says
-- whether — update now, later, or skip that version — and an update is fetched, unpacked beside
-- the plugin and swapped in by main.lua (env:install_update), keeping the reader's config file.
--
-- Checked when the reader asks, from the menu, and on its own at most once a day when the
-- device is already online: the plugin never turns wifi on by itself. A check of its own says
-- nothing unless there is something new; one the reader asked for always answers.

local json = require("readingtracker.vendor.json")

local Updater = {}
Updater.__index = Updater

-- Where releases come from unless the config says otherwise (update_repo = "owner/name").
Updater.DEFAULT_REPO = "N0V0C4IN3/ReadingTracker"

-- The file a plugin release carries; the release workflow attaches it under this name.
Updater.ASSET = "readingtracker.koplugin.zip"

-- Preferences, across documents.
Updater.AUTO = "update_check_automatically"   -- false turns the daily check off; on by default
Updater.LAST_CHECKED = "update_last_checked"  -- os.time() of the last answer from GitHub
Updater.SKIPPED = "update_skipped_version"    -- a version the reader said not to offer again

Updater.CHECK_EVERY = 24 * 60 * 60

-- env must provide: send, prompt, notify, clock, is_online, read_preference, save_preference,
-- installed_version() -> "0.3.0", install_update(url) -> true | nil, reason, ask_restart(text),
-- and config (update_repo is optional).
function Updater.new(env)
  return setmetatable({ env = env }, Updater)
end

-- "plugin-v0.3.0", "v0.3.0" or "0.3.0" as { 0, 3, 0 }; nil for anything that is not a version.
function Updater.parse_version(text)
  local digits = tostring(text or ""):match("^plugin%-v(%d[%d%.]*)$")
    or tostring(text or ""):match("^v?(%d[%d%.]*)$")
  if not digits or digits:sub(-1) == "." then
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
-- draft or a prerelease, with the plugin's zip attached. nil when there is none.
function Updater.latest(releases)
  local best
  for _, release in ipairs(type(releases) == "table" and releases or {}) do
    local version = type(release.tag_name) == "string"
      and release.tag_name:match("^plugin%-v") and Updater.parse_version(release.tag_name)
    local asset
    for _, attached in ipairs(release.assets or {}) do
      if attached.name == Updater.ASSET and type(attached.browser_download_url) == "string" then
        asset = attached.browser_download_url
      end
    end
    if version and asset and not release.draft and not release.prerelease
      and (not best or Updater.newer(version, best.parts)) then
      best = {
        parts = version,
        version = table.concat(version, "."),
        url = asset,
        page = release.html_url,
      }
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

-- The daily check, when the device comes online: only when it is on, a day after the last
-- answer, and quiet unless there is a version the reader has not already turned down.
function Updater:onNetworkConnected()
  if not self:automatic() then
    return
  end
  local last = tonumber(self.env:read_preference(Updater.LAST_CHECKED))
  if last and self.env:clock() - last < Updater.CHECK_EVERY then
    return
  end
  self:check(false)
end

-- Asks GitHub, and offers what is newer. `asked` is true when the reader chose it from the menu:
-- then every outcome is said, and a skipped version is offered again, since they asked.
function Updater:check(asked)
  local env = self.env
  local installed = Updater.parse_version(env:installed_version())

  if not env:is_online() then
    if asked then
      env:notify("Connect to wifi to check for updates")
    end
    return
  end

  local releases, problem = self:releases()
  if not releases then
    if asked then
      env:notify(problem)
    end
    return
  end
  env:save_preference(Updater.LAST_CHECKED, env:clock())

  local latest = Updater.latest(releases)
  if not latest or (installed and not Updater.newer(latest.parts, installed)) then
    if asked then
      env:notify("ReadingTracker " .. (env:installed_version() or "") .. " is the latest version")
    end
    return
  end

  if not asked and env:read_preference(Updater.SKIPPED) == latest.version then
    return
  end

  self:offer(latest)
end

function Updater:offer(latest)
  local env = self.env
  local have = env:installed_version()

  env:prompt({
    text = "ReadingTracker " .. latest.version .. " is available"
      .. (have and (" — you have " .. have) or "") .. ". Update now?",
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
  env:notify("Downloading ReadingTracker " .. latest.version .. "…")

  local ok, reason = env:install_update(latest.url)
  if not ok then
    env:notify("The update could not be installed (" .. tostring(reason)
      .. "). The plugin is as it was; it can be installed by hand from " .. tostring(latest.page))
    return
  end

  env:save_preference(Updater.SKIPPED, nil)
  env:ask_restart("ReadingTracker " .. latest.version .. " is installed. Restart KOReader to use it.")
end

-- GitHub's list of releases, newest first, or nil and what to tell the reader.
function Updater:releases()
  local env = self.env
  local repo = (env.config and env.config.update_repo) or Updater.DEFAULT_REPO
  local status, text = env:send({
    method = "GET",
    url = "https://api.github.com/repos/" .. repo .. "/releases?per_page=30",
    headers = {
      ["Accept"] = "application/vnd.github+json",
      -- GitHub refuses a request that does not say what is making it.
      ["User-Agent"] = "ReadingTracker-KOReader/" .. tostring(env:installed_version() or "unknown"),
    },
  })

  if status == nil then
    return nil, "GitHub could not be reached just now"
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
