-- ReadingTracker for KOReader: links the document being read to a book on the reader's shelf
-- and reports where they are as they read, through the Gateway, with a DeviceToken.
--
-- This file is the wiring only. Everything the plugin decides lives in readingtracker/app.lua
-- and is driven through the same handlers KOReader calls here, against an `env` that this file
-- builds from the real KOReader and that spec/support/fake_env.lua fakes for the tests.

local _ = require("gettext")
local Dispatcher = require("dispatcher")
local UIManager = require("ui/uimanager")
local NetworkMgr = require("ui/network/manager")
local Trapper = require("ui/trapper")
local ButtonDialog = require("ui/widget/buttondialog")
local InfoMessage = require("ui/widget/infomessage")
local Notification = require("ui/widget/notification")
local WidgetContainer = require("ui/widget/container/widgetcontainer")
local logger = require("logger")
local http = require("socket.http")
local ltn12 = require("ltn12")
local socketutil = require("socketutil")

local App = require("readingtracker.app")

local ReadingTracker = WidgetContainer:extend {
  name = "readingtracker",
  is_doc_only = false,
}

-- Where the plugin keeps what it knows about a document, inside KOReader's own per-document
-- settings, so it travels with the book and goes when the book's history goes.
local SETTINGS_KEY = "readingtracker"

-- The config file the reader copies in beside the plugin, with base_url and token. Missing is
-- not an error at load: the plugin sits quietly until something needs it, then says so once.
local function load_config()
  local ok, config = pcall(require, "readingtracker_config")
  if ok and type(config) == "table" and config.base_url and config.token then
    return config
  end
  return nil
end

function ReadingTracker:init()
  self.config = load_config()
  self.app = App.new(self:buildEnv())
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
end

-- The env: what the app takes from KOReader --------------------------------------------------

function ReadingTracker:buildEnv()
  local plugin = self
  local env = {}

  env.config = self.config or {}

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
      dismiss_callback = function() on_answer(nil) end,
    }
    UIManager:show(dialog)
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

  return env
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

function ReadingTracker:onReadingTrackerUnlink()
  if self.ui.document then
    self.app:unlink()
  end
end

-- Menu -----------------------------------------------------------------------------------------

function ReadingTracker:addToMainMenu(menu_items)
  menu_items.readingtracker = {
    text = _("ReadingTracker"),
    sorting_hint = "more_tools",
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
      text = _("Unlink this document"),
      enabled_func = function() return has_document and self.app:linked_entry() ~= nil end,
      callback = function() self.app:unlink() end,
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
  }
end

return ReadingTracker
