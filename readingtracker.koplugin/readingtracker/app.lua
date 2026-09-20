-- What the plugin does, apart from how KOReader is wired to it. Everything here is driven
-- through the same handlers KOReader calls — onReaderReady, onPageUpdate, onCloseDocument,
-- onSuspend, the menu — and talks to the outside only through `env`, which main.lua builds
-- from the real KOReader and spec/support/fake_env.lua fakes.
--
-- env provides:
--   config                      { base_url, token }
--   document                    { file, title, authors, identifiers, page, page_count } or nil
--   clock()                     seconds since the epoch
--   is_online()                 whether a request has any chance of getting through
--   read_setting(key), save_setting(key, value), flush_settings()
--                               what the plugin remembers about the open document
--   notify(text)                a passing notice
--   prompt(prompt, on_answer)   a question with choices; on_answer(choice_id or nil)
--   send(request)               the network: status, body | nil, "offline"

local Client = require("readingtracker.client")
local Isbn = require("readingtracker.isbn")

local App = {}
App.__index = App

-- What is remembered per document.
App.ENTRY = "entry_id"          -- the LibraryEntry this document is linked to
App.ENTRY_TITLE = "entry_title" -- so the menu can say what, without a request
App.NEVER = "never"             -- the reader said not to ask about this document again

function App.new(env)
  local app = setmetatable({}, App)
  app.env = env
  app.client = Client.new(env.config or {}, env)
  -- Set when the token is refused: nothing more is tried until the plugin is next loaded, and
  -- the reader has been told once rather than on every page turn.
  app.disabled = false
  return app
end

-- Linking -------------------------------------------------------------------------------------

function App:linked_entry()
  return self.env:read_setting(App.ENTRY)
end

function App:onReaderReady()
  if self.disabled or not self.env.document then
    return
  end

  if self:linked_entry() or self.env:read_setting(App.NEVER) then
    return
  end

  local isbn = Isbn.from_identifiers(self.env.document.identifiers)
  if isbn then
    self:link_by_isbn(isbn)
  end
end

-- An ISBN names one edition exactly, so a Book found by it is the book in the reader's hands.
-- Already on the shelf: linked without a word. Not yet: asked once — every PDF that gets
-- opened must not end up on the shelf.
function App:link_by_isbn(isbn)
  local books, err = self.client:search_by_isbn(isbn)
  if err then
    return self:complain(err)
  end

  local book = books[1]
  if not book then
    return
  end

  local entries, library_err = self.client:library()
  if library_err then
    return self:complain(library_err)
  end

  for _, entry in ipairs(entries) do
    if entry.bookId == book.id then
      return self:link(entry.id, book.title)
    end
  end

  self:offer_to_add(book)
end

function App:offer_to_add(book)
  self.env:prompt({
    text = string.format('Add "%s" to your ReadingTracker library and sync progress?', book.title),
    choices = {
      { id = "yes", label = "Yes" },
      { id = "not_now", label = "Not now" },
      { id = "never", label = "Never for this document" },
    },
  }, function(answer)
    if answer == "yes" then
      local entry, err = self.client:add_to_library(book.id)
      if err then
        return self:complain(err)
      end
      self:link(entry.id, book.title)
    elseif answer == "never" then
      self.env:save_setting(App.NEVER, true)
      self.env:flush_settings()
    end
    -- "Not now", or dismissed: nothing remembered, so the question comes back next opening.
  end)
end

function App:link(entry_id, title)
  self.env:save_setting(App.ENTRY, entry_id)
  self.env:save_setting(App.ENTRY_TITLE, title)
  self.env:save_setting(App.NEVER, nil)
  self.env:flush_settings()
  self.env:notify("Linked to " .. tostring(title))
end

function App:unlink()
  self.env:save_setting(App.ENTRY, nil)
  self.env:save_setting(App.ENTRY_TITLE, nil)
  self.env:flush_settings()
  self.env:notify("Unlinked from ReadingTracker")
end

-- Problems ------------------------------------------------------------------------------------

-- Says what went wrong, once, in the reader's terms. Offline is not a problem worth a notice:
-- an e-reader is offline most of the time, and the next chance will do.
function App:complain(err)
  if err.kind == Client.UNAUTHORIZED then
    self.disabled = true
    self.env:notify("ReadingTracker refused this device's token. Check the token in readingtracker_config.lua, or mint a new one on the Devices page.")
  elseif err.kind == Client.RATE_LIMITED then
    self.env:notify("ReadingTracker asked this device to wait a minute.")
  elseif err.kind == Client.OFFLINE then
    return
  elseif err.kind == Client.REFUSED and #err.reasons > 0 then
    self.env:notify("ReadingTracker said no: " .. table.concat(err.reasons, " "))
  else
    self.env:notify("ReadingTracker could not be reached just now.")
  end
end

return App
