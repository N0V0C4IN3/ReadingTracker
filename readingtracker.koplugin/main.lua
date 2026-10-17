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
local ltn12 = require("ltn12")
local socketutil = require("socketutil")

local App = require("readingtracker.app")
local UpdateWiring = require("readingtracker.update_wiring")
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
-- Whatever the file holds, even half filled in, so update_repo works before the plugin is set up.
local function load_config()
  local ok, config = pcall(require, "readingtracker_config")
  return ok and type(config) == "table" and config or {}
end

function ReadingTracker:init()
  local file = load_config()
  self.config = (file.base_url and file.token) and file or nil
  self.update_repo = file.update_repo
  -- The plugin's own settings, across documents: the sync interval and the like.
  self.preferences = LuaSettings:open(DataStorage:getSettingsDir() .. "/readingtracker.lua")
  local env = self:buildEnv()
  self.app = App.new(env)
  self.updater = Updater.new(env)
  self.ui.menu:registerToMainMenu(self)
  self:onDispatcherRegisterActions()
  self:addToHighlightMenu()
end

-- Quotes ---------------------------------------------------------------------------------------

-- "Save to ReadingTracker" in the menu KOReader shows for selected text and, from its "…", for a
-- highlight already made. Only while reading: the file manager has no highlights.
function ReadingTracker:addToHighlightMenu()
  -- Annotations, with their creation time, came in KOReader 2024.07; before them the menu's
  -- buttons were called differently too, so an older KOReader gets no button at all.
  if not (self.ui.highlight and self.ui.highlight.addToHighlightDialog and self.ui.annotation) then
    return
  end
  local plugin = self
  self.ui.highlight:addToHighlightDialog("12_readingtracker", function(highlight, index)
    return {
      text = _("Save to ReadingTracker"),
      callback = function() plugin:saveHighlight(highlight, index) end,
    }
  end)
end

-- A selection is made a highlight first, so the Quote has a highlight on the device to stand for:
-- the same one, with the same identity, whenever it is saved again. It is made the way
-- KOReader's own Highlight button makes it.
function ReadingTracker:saveHighlight(highlight, index)
  if not self:configured() then
    return highlight:onClose()
  end
  index = index or highlight:saveHighlight(true)
  local item = index and self.ui.annotation.annotations[index]
  highlight:onClose()
  if not item then
    return UIManager:show(Notification:new {
      text = _("This selection could not be made a highlight, so it was not kept."),
      timeout = 3,
    })
  end
  local described = self:describeHighlight(item)
  Trapper:wrap(function() self.app:save_quote(described) end)
end

-- An annotation as app.lua takes a highlight. The page is where it begins in the layout as it is
-- now, as the Bookmark's page is: KOReader's own pageno goes stale after a change of font until
-- it next saves, and a PDF's page never moves.
function ReadingTracker:describeHighlight(item)
  local page
  if type(item.pos0) == "table" then
    page = item.pos0.page
  elseif item.pos0 and self.ui.document.getPageFromXPointer then
    page = self.ui.document:getPageFromXPointer(item.pos0)
  end
  return {
    datetime = item.datetime,
    start = item.pos0,
    passage = item.text,
    note = item.note,
    page = tonumber(page) or tonumber(item.pageno),
    chapter = item.chapter,
  }
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
      source = request.body and ltn12.source.string(request.body) or nil,
      sink = socketutil.table_sink(sink),
    }
    socketutil:reset_timeout()

    if type(code) ~= "number" then
      logger.warn("readingtracker: request failed", request.method, request.url, code)
      return nil, "offline"
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

  -- What updating the plugin takes besides: GitHub, the plugin's files, a few dialogs.
  return UpdateWiring.wire(env, plugin)
end

-- Updating ---------------------------------------------------------------------------------------

-- The folder the plugin runs from: KOReader's plugin loader says, and failing that, this file.
function ReadingTracker:folder()
  local folder = self.path or debug.getinfo(1, "S").source:match("^@(.*)/[^/]*$")
  return folder and folder:gsub("/+$", "") or nil
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
