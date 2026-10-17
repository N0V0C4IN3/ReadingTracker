-- Putting a release's zip in place of the plugin, as one move that either happens whole or
-- leaves the plugin as it was: downloaded, its entries checked, unpacked beside the plugin,
-- checked to be a whole plugin, given the reader's config file, and only then swapped in, the
-- old folder kept until the new one is in place.
--
-- Every decision is here; readingtracker/device.lua gives it the device's files and network:
--   plugin_folder()             where the plugin runs from, e.g. "plugins/readingtracker.koplugin"
--   cache_dir()                 somewhere to put the download
--   fetch(url, options)         one GET to GitHub over TLS checked against its certificate, no
--                               redirect followed; options.into a path to write the body to:
--                               status, { location, body } | nil, reason
--   entries(zip)                what a zip holds, { { path, mode }, ... } | nil, reason
--   extract(zip, dir)           true | nil, reason
--   exists(path), copy(from, to), rename(from, to), remove(path)
--                               files and folders; copy, rename answer true | nil, reason

local Installer = {}
Installer.__index = Installer

Installer.CONFIG = "readingtracker_config.lua"
Installer.HOPS = 5

function Installer.new(env, repo)
  return setmetatable({ env = env, repo = repo }, Installer)
end

-- Only a release's own file on GitHub is fetched; its redirect may lead anywhere over https.
-- GitHub's owner and repository names are not case-sensitive, so neither is this.
function Installer.from_release(url, repo)
  local prefix = ("https://github.com/" .. repo .. "/releases/download/"):lower()
  return type(url) == "string" and url:sub(1, #prefix):lower() == prefix
end

-- A Location header against the URL that sent it: absolute, from the scheme ("//host/x"), or
-- from the host's root ("/x"). nil for one relative to the path, which GitHub never sends.
function Installer.resolve(base, location)
  if location:match("^%a[%w+.-]*://") then
    return location
  end
  local scheme, origin = base:match("^((%a[%w+.-]*):)//[^/]+"), base:match("^(%a[%w+.-]*://[^/]+)")
  if location:sub(1, 2) == "//" and scheme then
    return scheme .. location
  end
  if location:sub(1, 1) == "/" and origin then
    return origin .. location
  end
  return nil
end

-- The first entry a plugin's zip must not hold, or nil when all are fine: only files and
-- folders, none absolute, none climbing out with "..".
function Installer.refused(entries)
  for _, entry in ipairs(entries) do
    local path = tostring(entry.path)
    local inside = path ~= "" and path:sub(1, 1) ~= "/" and not ("/" .. path .. "/"):find("/../", 1, true)
    if not inside or (entry.mode ~= "file" and entry.mode ~= "directory") then
      return path
    end
  end
  return nil
end

function Installer:download(url, path)
  for _ = 1, Installer.HOPS do
    if not url then
      return nil, "the download was redirected somewhere it could not follow"
    end
    if url:sub(1, 8) ~= "https://" then
      return nil, "the download was sent somewhere that is not https"
    end
    local status, response = self.env:fetch(url, { into = path })
    if status == nil then
      return nil, response or "the download did not arrive"
    elseif status == 200 then
      return true
    elseif status >= 300 and status < 400 and response and response.location then
      url = Installer.resolve(url, response.location)
    else
      return nil, "the download answered " .. tostring(status)
    end
  end
  return nil, "the download kept being redirected"
end

function Installer:unpack(zip, dir)
  local env = self.env
  local entries, reason = env:entries(zip)
  if not entries then
    return nil, reason
  end
  local refused = Installer.refused(entries)
  if refused then
    return nil, "the zip holds something that is not the plugin's (" .. refused .. ")"
  end
  return env:extract(zip, dir)
end

-- true; or nil, the reason, and — only when the old plugin could not be put back — where it is.
function Installer:install(url)
  local env = self.env
  if not Installer.from_release(url, self.repo) then
    return nil, "the release's file is not on GitHub"
  end

  local folder = env:plugin_folder()
  local parent = folder and folder:match("^(.*)/[^/]+$")
  if not parent then
    return nil, "the plugin's folder could not be found"
  end

  local zip = env:cache_dir() .. "/readingtracker-update.zip"
  local staging = parent .. "/readingtracker.koplugin.update"
  local old = parent .. "/readingtracker.koplugin.old"
  local unpacked = staging .. "/readingtracker.koplugin"

  local function clean()
    env:remove(zip)
    env:remove(staging)
  end

  clean()
  local ok, reason = self:download(url, zip)
  if ok then
    ok, reason = self:unpack(zip, staging)
  end
  if ok and not (env:exists(unpacked .. "/main.lua") and env:exists(unpacked .. "/_meta.lua")) then
    ok, reason = nil, "the download is not the plugin"
  end
  if ok and env:exists(folder .. "/" .. Installer.CONFIG) then
    ok, reason = env:copy(folder .. "/" .. Installer.CONFIG, unpacked .. "/" .. Installer.CONFIG)
    if not ok then
      reason = "your config could not be carried over (" .. tostring(reason) .. ")"
    end
  end
  if not ok then
    clean()
    return nil, reason
  end

  env:remove(old)
  ok, reason = env:rename(folder, old)
  if not ok then
    clean()
    return nil, "the plugin could not be moved aside (" .. tostring(reason) .. ")"
  end

  local placed, place_reason = env:rename(unpacked, folder)
  if not placed then
    local back = env:rename(old, folder)
    clean()
    if not back then
      return nil, "the new plugin could not be put in place (" .. tostring(place_reason) .. ")", old
    end
    return nil, "the new plugin could not be put in place (" .. tostring(place_reason) .. ")"
  end

  clean()
  env:remove(old)
  return true
end

return Installer
