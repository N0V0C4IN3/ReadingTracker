-- What the updater and installer take from the device, wired from the real KOReader: GitHub
-- over checked TLS, the plugin's files, and the few dialogs an update needs. Like main.lua this
-- is wiring only; what it does with them is decided in updater.lua and installer.lua, which
-- spec/support/fake_env.lua drives without a Kindle.

local DataStorage = require("datastorage")
local InfoMessage = require("ui/widget/infomessage")
local UIManager = require("ui/uimanager")
local http = require("socket.http")
local lfs = require("libs/libkoreader-lfs")
local logger = require("logger")
local socketutil = require("socketutil")
local util = require("util")

local TrustedHttps = require("readingtracker.trusted_https")

local UpdateWiring = {}

-- Seconds: a pause in the transfer, and the whole of one request. A plugin's zip is small.
local TIMEOUT = { block = 15, total = 120 }

-- Removes a file, a link, or a folder and all it holds, never following a link into what it
-- points at — KOReader's purgeDir follows links below the top.
local function remove_tree(path)
  local mode = lfs.symlinkattributes(path, "mode")
  if mode == "directory" then
    for name in lfs.dir(path) do
      if name ~= "." and name ~= ".." then
        remove_tree(path .. "/" .. name)
      end
    end
    lfs.rmdir(path)
  elseif mode then
    os.remove(path)
  end
end

local function open_archive(zip)
  local has, Archiver = pcall(require, "ffi/archiver")
  if not has then
    return nil, "this KOReader cannot unpack a zip by itself"
  end
  local archive = Archiver.Reader:new()
  if not archive:open(zip) then
    return nil, "the download is not a zip"
  end
  return archive
end

function UpdateWiring.wire(env, plugin)
  -- From _meta.lua, which KOReader's plugin loader reads once at start: after an update swaps
  -- the folder, the code running is still this version until the restart.
  env.version = plugin.version

  local cafile = plugin:folder() .. "/readingtracker/certs/roots.pem"

  function env:ask_restart(text)
    UIManager:askForRestart(text)
  end

  function env:tell(text)
    logger.warn("readingtracker:", text)
    UIManager:show(InfoMessage:new { text = text })
  end

  -- Shown and painted at once: the download that follows blocks before the next repaint.
  function env:working(text)
    local message = InfoMessage:new { text = text }
    UIManager:show(message)
    UIManager:forceRePaint()
    return function() UIManager:close(message) end
  end

  function env:plugin_folder()
    return plugin:folder()
  end

  function env:cache_dir()
    local cache = DataStorage:getDataDir() .. "/cache"
    util.makePath(cache)
    return cache
  end

  function env:fetch(url, options)
    options = options or {}
    -- LuaSocket would hand the proxy the TLS meant for GitHub, and the check would fail on it.
    if http.PROXY then
      return nil, "updates cannot go through the proxy set in KOReader's network settings"
    end

    local file, body, sink = nil, {}, nil
    if options.into then
      local opened, open_err = io.open(options.into, "wb")
      if not opened then
        return nil, tostring(open_err)
      end
      file, sink = opened, socketutil.file_sink(opened)
    else
      sink = socketutil.table_sink(body)
    end

    local headers = { ["User-Agent"] = "ReadingTracker-KOReader/" .. tostring(env.version) }
    for name, value in pairs(options.headers or {}) do
      headers[name] = value
    end

    socketutil:set_timeout(TIMEOUT.block, TIMEOUT.total)
    local ran, result, code, response_headers = pcall(http.request, {
      url = url,
      method = "GET",
      headers = headers,
      sink = sink,
      redirect = false,
      create = TrustedHttps.create(cafile, TIMEOUT.block),
    })
    socketutil:reset_timeout()
    -- The sink closes the file at the end; not when the request failed before it.
    if file then
      pcall(file.close, file)
    end

    if not ran or type(code) ~= "number" then
      local why = tostring(ran and code or result)
      logger.warn("readingtracker: GitHub request failed", url, why)
      return nil, why
    end
    return code, {
      location = response_headers and response_headers.location,
      body = not file and table.concat(body) or nil,
    }
  end

  -- A zip by its first bytes, before libarchive, which would open a tar and its hard links too.
  function env:entries(zip)
    local handle = io.open(zip, "rb")
    local head = handle and handle:read(4)
    if handle then
      handle:close()
    end
    if head ~= "PK\3\4" then
      return nil, "the download is not a zip"
    end

    local archive, reason = open_archive(zip)
    if not archive then
      return nil, reason
    end
    local list = {}
    for entry in archive:iterate() do
      table.insert(list, { path = entry.path, mode = entry.mode })
    end
    local err = archive.err
    archive:close()
    if err then
      return nil, tostring(err)
    end
    return list
  end

  function env:extract(zip, dir)
    local archive, reason = open_archive(zip)
    if not archive then
      return nil, reason
    end
    util.makePath(dir)
    for entry in archive:iterate() do
      if not archive:extractToPath(entry.path, dir .. "/" .. entry.path) then
        local err = archive.err or ("could not unpack " .. entry.path)
        archive:close()
        return nil, tostring(err)
      end
    end
    local err = archive.err
    archive:close()
    if err then
      return nil, tostring(err)
    end
    return true
  end

  function env:exists(path)
    return lfs.attributes(path, "mode") ~= nil
  end

  -- Every write checked: a full disk must not leave the reader a cut-off config.
  function env:copy(from, to)
    local input = io.open(from, "rb")
    if not input then
      return nil, "cannot read " .. from
    end
    local data = input:read("*a")
    input:close()
    local output, open_err = io.open(to, "wb")
    if not output then
      return nil, tostring(open_err)
    end
    local wrote, write_err = output:write(data)
    local closed, close_err = output:close()
    if not wrote or not closed then
      os.remove(to)
      return nil, tostring(write_err or close_err)
    end
    return true
  end

  function env:rename(from, to)
    local ok, err = os.rename(from, to)
    if ok then
      return true
    end
    return nil, tostring(err)
  end

  function env:remove(path)
    remove_tree(path)
  end

  return env
end

return UpdateWiring
