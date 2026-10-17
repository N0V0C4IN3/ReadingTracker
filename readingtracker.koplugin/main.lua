-- ReadingTracker for KOReader: links the document being read to a book on the reader's shelf
-- and reports where they are as they read, through the Gateway, with a DeviceToken.
--
-- This file is the wiring only. Everything the plugin decides lives in readingtracker/app.lua
-- and is driven through the same handlers KOReader calls here, against an `env` that this file
-- builds from the real KOReader and that spec/support/fake_env.lua fakes for the tests.

local _ = require("gettext")
local DataStorage = require("datastorage")
local Dispatcher = require("dispatcher")
local LuaSettings = require("luasettings")
local UIManager = require("ui/uimanager")
local NetworkMgr = require("ui/network/manager")
local Trapper = require("ui/trapper")
local ButtonDialog = require("ui/widget/buttondialog")
local InfoMessage = require("ui/widget/infomessage")
local Menu = require("ui/widget/menu")
local MultiInputDialog = require("ui/widget/multiinputdialog")
local Notification = require("ui/widget/notification")
local SpinWidget = require("ui/widget/spinwidget")
local Screen = require("device").screen
local WidgetContainer = require("ui/widget/container/widgetcontainer")
local logger = require("logger")
local http = require("socket.http")
local lfs = require("libs/libkoreader-lfs")
local ltn12 = require("ltn12")
local socketutil = require("socketutil")
local ffiUtil = require("ffi/util")
local util = require("util")

local App = require("readingtracker.app")
local TrustedHttps = require("readingtracker.trusted_https")
local Updater = require("readingtracker.updater")

local ReadingTracker = WidgetContainer:extend {
  name = "readingtracker",
  is_doc_only = false,
}

-- Where the plugin keeps what it knows about a document, inside KOReader's own per-document
-- settings, so it travels with the book and goes when the book's history goes.
local SETTINGS_KEY = "readingtracker"

-- The config file the reader copies in beside the plugin, with base_url and token. Missing is
-- not an error at load: the plugin sits quietly until something needs it, then says so once.
-- Answers the config when it is set up, and whatever the file holds either way, so update_repo
-- works on a device that has not been set up.
local function load_config()
  local ok, config = pcall(require, "readingtracker_config")
  if not ok or type(config) ~= "table" then
    return nil, {}
  end
  if config.base_url and config.token then
    return config, config
  end
  return nil, config
end

function ReadingTracker:init()
  local file
  self.config, file = load_config()
  self.update_repo = file.update_repo
  -- Read once: after an update swaps the folder, the code running is still this version.
  self.version = self:readVersion()
  -- The plugin's own settings, across documents: the sync interval and the like.
  self.preferences = LuaSettings:open(DataStorage:getSettingsDir() .. "/readingtracker.lua")
  local env = self:buildEnv()
  self.app = App.new(env)
  self.updater = Updater.new(env)
  self.ui.menu:registerToMainMenu(self)
  self:onDispatcherRegisterActions()
end

function ReadingTracker:onDispatcherRegisterActions()
  Dispatcher:registerAction("readingtracker_unlink", {
    category = "none",
    event = "ReadingTrackerUnlink",
    title = _("ReadingTracker: unlink this document"),
    reader = true,
  })
  Dispatcher:registerAction("readingtracker_sync", {
    category = "none",
    event = "ReadingTrackerSync",
    title = _("ReadingTracker: sync now"),
    reader = true,
  })
  Dispatcher:registerAction("readingtracker_check_for_update", {
    category = "none",
    event = "ReadingTrackerCheckForUpdate",
    title = _("ReadingTracker: check for updates"),
    general = true,
  })
end

-- The env: what the app takes from KOReader --------------------------------------------------

function ReadingTracker:buildEnv()
  local plugin = self
  local env = {}

  env.config = self.config or { update_repo = self.update_repo }

  function env:clock()
    return os.time()
  end

  function env:is_online()
    return NetworkMgr:isConnected()
  end

  function env:read_setting(key)
    local all = plugin:docSettings()
    return all and all[key] or nil
  end

  function env:save_setting(key, value)
    local settings = plugin.ui and plugin.ui.doc_settings
    if not settings then
      return
    end
    local all = settings:readSetting(SETTINGS_KEY) or {}
    all[key] = value
    settings:saveSetting(SETTINGS_KEY, all)
  end

  function env:flush_settings()
    local settings = plugin.ui and plugin.ui.doc_settings
    if settings then
      settings:flush()
    end
  end

  function env:read_preference(key)
    return plugin.preferences:readSetting(key)
  end

  function env:save_preference(key, value)
    plugin.preferences:saveSetting(key, value)
    plugin.preferences:flush()
  end

  -- Updating: what readingtracker/updater.lua and installer.lua take from the device --------

  env.version = self.version

  function env:ask_restart(text)
    UIManager:askForRestart(text)
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

  -- One GET into a file, no redirect followed, over TLS checked against GitHub's roots.
  function env:fetch(url, path)
    local file, open_err = io.open(path, "wb")
    if not file then
      return nil, tostring(open_err)
    end
    socketutil:set_timeout(15, 120)
    local ok, result, code, headers = pcall(http.request, {
      url = url,
      method = "GET",
      headers = { ["User-Agent"] = "ReadingTracker-KOReader/" .. tostring(env.version) },
      sink = socketutil.file_sink(file),
      redirect = false,
      create = TrustedHttps.create(plugin:cafile(), 15),
    })
    socketutil:reset_timeout()
    -- The sink closes the file at the end; not when the request failed before it.
    pcall(file.close, file)

    if not ok or type(code) ~= "number" then
      local why = tostring(ok and code or result)
      logger.warn("readingtracker: download failed", url, why)
      return nil, "the download did not arrive (" .. why .. ")"
    end
    return code, headers and headers.location
  end

  -- With KOReader's own archiver. Every entry is looked at before anything is written: a link,
  -- or a path that climbs out of the folder, refuses the whole zip.
  function env:unpack(zip, dir)
    local has, Archiver = pcall(require, "ffi/archiver")
    if not has then
      return nil, "this KOReader cannot unpack a zip by itself"
    end
    local archive = Archiver.Reader:new()
    if not archive:open(zip) then
      return nil, "the download is not a zip"
    end
    for entry in archive:iterate() do
      local inside = entry.path:sub(1, 1) ~= "/" and not ("/" .. entry.path .. "/"):find("/../", 1, true)
      if not inside or (entry.mode ~= "file" and entry.mode ~= "directory") then
        archive:close()
        return nil, "the zip holds something that is not the plugin's (" .. entry.path .. ")"
      end
    end
    util.makePath(dir)
    for entry in archive:iterate() do
      if not archive:extractToPath(entry.path, dir .. "/" .. entry.path) then
        break
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

  -- A link is removed as a link, never followed into what it points at.
  function env:remove(path)
    local mode = lfs.symlinkattributes(path, "mode")
    if mode == "directory" then
      ffiUtil.purgeDir(path)
    elseif mode then
      os.remove(path)
    end
  end

  function env:notify(text)
    UIManager:show(Notification:new { text = text, timeout = 3 })
  end

  -- One dialog, its choices as buttons, the answer handed back as the choice's id. Dismissing
  -- it — tapping outside — answers nil, which the app treats as "not now".
  function env:prompt(prompt, on_answer)
    local dialog
    local rows = {}
    for _, choice in ipairs(prompt.choices) do
      table.insert(rows, { {
        text = choice.label,
        callback = function()
          UIManager:close(dialog)
          Trapper:wrap(function() on_answer(choice.id) end)
        end,
      } })
    end
    dialog = ButtonDialog:new {
      title = prompt.text,
      title_align = "center",
      buttons = rows,
      dismissable = true,
      tap_close_callback = function() on_answer(nil) end,
    }
    UIManager:show(dialog)
  end

  -- A form of text fields, answered as { id = value } — or nil from Cancel.
  function env:ask_text(form, on_submit)
    local dialog
    local fields = {}
    for _, field in ipairs(form.fields) do
      table.insert(fields, { text = field.value or "", hint = field.label })
    end

    dialog = MultiInputDialog:new {
      title = form.title,
      fields = fields,
      buttons = { {
        {
          text = _("Cancel"),
          id = "close",
          callback = function()
            UIManager:close(dialog)
            on_submit(nil)
          end,
        },
        {
          text = form.submit or _("OK"),
          is_enter_default = true,
          callback = function()
            local typed = dialog:getFields()
            UIManager:close(dialog)
            local values = {}
            for index, field in ipairs(form.fields) do
              values[field.id] = typed[index] or ""
            end
            Trapper:wrap(function() on_submit(values) end)
          end,
        },
      } },
    }
    UIManager:show(dialog)
    dialog:onShowKeyboard()
  end

  -- A full-screen list to pick one item from; closing it without picking answers nil.
  function env:pick(list, on_pick)
    local menu
    local picked = false
    local items = {}
    for _, item in ipairs(list.items) do
      table.insert(items, {
        text = item.detail and (item.text .. " — " .. item.detail) or item.text,
        id = item.id,
      })
    end

    menu = Menu:new {
      title = list.title,
      item_table = items,
      is_borderless = true,
      is_popout = false,
      width = Screen:getWidth(),
      height = Screen:getHeight(),
      onMenuSelect = function(_, item)
        picked = true
        UIManager:close(menu)
        Trapper:wrap(function() on_pick(item.id) end)
      end,
      close_callback = function()
        if not picked then
          on_pick(nil)
        end
      end,
    }
    UIManager:show(menu)
  end

  -- A blocking request; the app is always run inside a Trapper so this does not freeze the
  -- screen. Timeouts are short: a Kindle on flaky wifi should give up, not hang.
  function env:send(request)
    if not NetworkMgr:isConnected() then
      return nil, "offline"
    end

    local sink = {}
    socketutil:set_timeout(8, 15)
    local _, code = http.request {
      url = request.url,
      method = request.method,
      headers = plugin:headersFor(request),
      -- GitHub, for the updater: its certificate checked, not taken on trust.
      create = request.trusted and TrustedHttps.create(plugin:cafile(), 8) or nil,
      source = request.body and ltn12.source.string(request.body) or nil,
      sink = socketutil.table_sink(sink),
    }
    socketutil:reset_timeout()

    if type(code) ~= "number" then
      logger.warn("readingtracker: request failed", request.method, request.url, code)
      return nil, request.trusted and tostring(code) or "offline"
    end

    return code, table.concat(sink)
  end

  -- Read afresh each time: KOReader swaps the document under the plugin, and the app must
  -- never hold on to the last one.
  setmetatable(env, {
    __index = function(_, key)
      if key == "document" then
        return plugin:describeDocument()
      end
    end,
  })

  return env
end

-- Updating ---------------------------------------------------------------------------------------

-- The folder the plugin runs from: KOReader's plugin loader says, and failing that, this file.
function ReadingTracker:folder()
  local folder = self.path or debug.getinfo(1, "S").source:match("^@(.*)/[^/]*$")
  return folder and folder:gsub("/+$", "") or nil
end

-- The version in _meta.lua, which the release workflow checks against the release's tag.
function ReadingTracker:readVersion()
  local folder = self:folder()
  local ok, meta = pcall(dofile, folder and (folder .. "/_meta.lua") or "")
  return ok and type(meta) == "table" and meta.version or nil
end

-- The roots GitHub's certificates are checked against.
function ReadingTracker:cafile()
  return self:folder() .. "/readingtracker/certs/github.pem"
end

function ReadingTracker:onReadingTrackerCheckForUpdate()
  Trapper:wrap(function() self.updater:check() end)
end

function ReadingTracker:headersFor(request)
  local headers = {}
  for name, value in pairs(request.headers or {}) do
    headers[name] = value
  end
  if request.body then
    headers["Content-Length"] = tostring(#request.body)
  end
  return headers
end

function ReadingTracker:docSettings()
  local settings = self.ui and self.ui.doc_settings
  return settings and settings:readSetting(SETTINGS_KEY) or nil
end

function ReadingTracker:describeDocument()
  local document = self.ui and self.ui.document
  if not document then
    return nil
  end

  local props = document:getProps() or {}
  local page_count = document:getPageCount()
  local page = self.ui.getCurrentPage and self.ui:getCurrentPage() or nil

  return {
    file = document.file,
    title = props.title,
    authors = props.authors,
    identifiers = props.identifiers,
    page = page,
    page_count = page_count,
  }
end

-- What has to be true before anything is tried: a config file with both values in it.
function ReadingTracker:configured()
  if self.config then
    return true
  end

  UIManager:show(InfoMessage:new {
    text = _("ReadingTracker is not set up on this device: copy readingtracker_config.example.lua to readingtracker_config.lua and fill in the Gateway address and a device token."),
    timeout = 6,
  })
  return false
end

-- Events ---------------------------------------------------------------------------------------

function ReadingTracker:onReaderReady()
  if not self.config then
    return
  end
  -- A moment after the document is up, and off the UI thread's critical path: the first thing
  -- a reader wants from an opened book is the book.
  UIManager:scheduleIn(2, function()
    Trapper:wrap(function() self.app:onReaderReady() end)
  end)
end

-- A page turn, in either of the two forms KOReader sends it: PageUpdate for paged documents,
-- PosUpdate for reflowable ones. The app decides whether it is time to report; the decision is
-- taken a moment later so turning pages quickly never queues a report per page.
function ReadingTracker:onPageUpdate(page)
  self:pageTurned(page)
end

function ReadingTracker:onPosUpdate(_, page)
  self:pageTurned(page)
end

function ReadingTracker:pageTurned(page)
  if not self.config or not page then
    return
  end

  if self.pending_turn then
    UIManager:unschedule(self.pending_turn)
  end

  self.pending_turn = function()
    self.pending_turn = nil
    Trapper:wrap(function() self.app:onPageUpdate(page) end)
  end
  UIManager:scheduleIn(2, self.pending_turn)
end

-- A turn still waiting its moment is not to be lost to closing or sleeping: it is handed to
-- the app now, so what gets reported is the page the reader is actually on.
function ReadingTracker:settlePendingTurn()
  if self.pending_turn then
    UIManager:unschedule(self.pending_turn)
    local turn = self.pending_turn
    self.pending_turn = nil
    turn()
  end
end

function ReadingTracker:onCloseDocument()
  if not self.config then
    return
  end
  self:settlePendingTurn()
  self.app:onCloseDocument()
end

function ReadingTracker:onSuspend()
  if not self.config then
    return
  end
  self:settlePendingTurn()
  self.app:onSuspend()
end

function ReadingTracker:onNetworkConnected()
  if self.config and self.ui.document then
    Trapper:wrap(function() self.app:onNetworkConnected() end)
  end
  -- Whether or not a book is open or the plugin set up: an update is the plugin's own business.
  Trapper:wrap(function() self.updater:onNetworkConnected() end)
end

-- KOReader's own "you reached the end" hook; its action setting is left to it, this only asks
-- the shelf's question.
function ReadingTracker:onEndOfBook()
  if self.config and self.ui.document then
    Trapper:wrap(function() self.app:onEndOfBook() end)
  end
end

function ReadingTracker:onReadingTrackerUnlink()
  if self.ui.document then
    self.app:unlink()
  end
end

function ReadingTracker:onReadingTrackerSync()
  if self.ui.document and self:configured() then
    Trapper:wrap(function() self.app:sync_now() end)
  end
end

-- Menu -----------------------------------------------------------------------------------------

-- While reading, the menu sits at the end of the first tab, where the book's own things —
-- contents, bookmarks — are, and where the reader reaches for it; a plugin with no hint lands
-- there too, but with a "NEW:" stuck in front of its name for good. The file manager's first
-- tab is its own settings, so there it stays with the other tools.
function ReadingTracker:addToMainMenu(menu_items)
  menu_items.readingtracker = {
    text = _("ReadingTracker"),
    sorting_hint = self.ui and self.ui.document and "navi" or "more_tools",
    sub_item_table_func = function() return self:menuItems() end,
  }
end

function ReadingTracker:menuItems()
  local has_document = self.ui and self.ui.document ~= nil

  return {
    {
      text_func = function()
        local title = self.app.env:read_setting(App.ENTRY_TITLE)
        if title then
          return _("Linked to: ") .. title
        end
        return _("Not linked to a book")
      end,
      enabled_func = function() return false end,
    },
    {
      text_func = function()
        return self.app:linked_entry() and _("Link to a different book…") or _("Link to a book…")
      end,
      enabled_func = function() return has_document and self.config ~= nil end,
      callback = function()
        Trapper:wrap(function() self.app:link_by_search() end)
      end,
    },
    {
      text = _("Unlink this document"),
      enabled_func = function() return has_document and self.app:linked_entry() ~= nil end,
      callback = function() self.app:unlink() end,
    },
    {
      text = _("Sync now"),
      enabled_func = function() return has_document and self.app:linked_entry() ~= nil end,
      callback = function() self:onReadingTrackerSync() end,
    },
    {
      text = _("Reading status…"),
      enabled_func = function() return has_document and self.app:linked_entry() ~= nil end,
      callback = function()
        if self:configured() then
          Trapper:wrap(function() self.app:change_status() end)
        end
      end,
    },
    {
      text_func = function()
        local minutes = tonumber(self.preferences:readSetting(App.INTERVAL)) or App.DEFAULT_INTERVAL_MINUTES
        return _("Report while reading every: ") .. minutes .. _(" min")
      end,
      keep_menu_open = true,
      callback = function(menu_instance)
        UIManager:show(SpinWidget:new {
          title_text = _("Report while reading every"),
          info_text = _("Where you are is also reported whenever you close the book or the device sleeps."),
          value = tonumber(self.preferences:readSetting(App.INTERVAL)) or App.DEFAULT_INTERVAL_MINUTES,
          value_min = 1,
          value_max = 120,
          value_step = 1,
          value_hold_step = 5,
          unit = _("min"),
          ok_text = _("Set"),
          callback = function(spin)
            self.preferences:saveSetting(App.INTERVAL, spin.value)
            self.preferences:flush()
            if menu_instance then
              menu_instance:updateItems()
            end
          end,
        })
      end,
    },
    {
      text = _("Check setup"),
      callback = function()
        if self:configured() then
          UIManager:show(InfoMessage:new {
            text = _("ReadingTracker is set up. Gateway: ") .. tostring(self.config.base_url),
            timeout = 4,
          })
        end
      end,
    },
    {
      text_func = function()
        return _("Check for updates") .. " (" .. tostring(self.version or "?") .. ")"
      end,
      callback = function() self:onReadingTrackerCheckForUpdate() end,
    },
    {
      text = _("Check for updates once a day on wifi"),
      checked_func = function() return self.updater:automatic() end,
      callback = function() self.updater:set_automatic(not self.updater:automatic()) end,
    },
  }
end

return ReadingTracker
