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
--   ask_text(form, on_submit)   a form of text fields; on_submit({ id = value } or nil)
--   pick(list, on_pick)         a list to choose from; on_pick(item_id or nil)
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

  -- Nothing to link with while offline; the next opening with wifi will ask.
  if not self.env:is_online() then
    return
  end

  local isbn = Isbn.from_identifiers(self.env.document.identifiers)
  if isbn then
    self:link_by_isbn(isbn)
  else
    self:offer_to_search()
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
    -- An ISBN nobody has heard of is no better than none: the reader can search instead.
    return self:offer_to_search()
  end

  local entry_id, found = self:shelf_entry_for(book.id)
  if found == nil then
    return
  end

  if entry_id then
    return self:link(entry_id, book.title)
  end

  self:offer_to_add(book)
end

-- The reader's entry for a Book, if they have one: id, true; none: nil, true; could not be
-- asked: nil, nil (already complained about).
function App:shelf_entry_for(book_id)
  local entries, err = self.client:library()
  if err then
    self:complain(err)
    return nil, nil
  end

  for _, entry in ipairs(entries) do
    if entry.bookId == book_id then
      return entry.id, true
    end
  end

  return nil, true
end

-- Puts a Book on the shelf if it is not there and links the document to the entry either way.
function App:shelve_and_link(book)
  local entry_id, found = self:shelf_entry_for(book.id)
  if found == nil then
    return
  end

  if not entry_id then
    local entry, err = self.client:add_to_library(book.id)
    if err then
      return self:complain(err)
    end
    entry_id = entry.id
  end

  self:link(entry_id, book.title)
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

-- Linking by search --------------------------------------------------------------------------

-- No ISBN, or one the Catalog does not know: asked once, the way the ISBN flow asks, but
-- leading into a search instead of a yes.
function App:offer_to_search()
  self.env:prompt({
    text = string.format('Link "%s" to ReadingTracker?', self:document_title()),
    choices = {
      { id = "search", label = "Search" },
      { id = "not_now", label = "Not now" },
      { id = "never", label = "Never for this document" },
    },
  }, function(answer)
    if answer == "search" then
      self:link_by_search()
    elseif answer == "never" then
      self.env:save_setting(App.NEVER, true)
      self.env:flush_settings()
    end
  end)
end

-- A title/author search, filled in from the document, with the results to pick from. Also
-- what the menu's link / re-link does, in which case any existing link is replaced.
function App:link_by_search(title, author)
  self.env:ask_text({
    title = "Find the book in the library",
    fields = {
      { id = "title", label = "Title", value = title or self:document_title() },
      { id = "author", label = "Author", value = author or self:document_author() },
    },
    submit = "Search",
  }, function(values)
    if not values then
      return
    end

    local books, err = self.client:search(values.title, values.author)
    if err then
      return self:complain(err)
    end

    if #books == 0 then
      return self:offer_to_add_by_hand(values.title, values.author)
    end

    self:pick_from(books)
  end)
end

function App:pick_from(books)
  local items = {}
  for _, book in ipairs(books) do
    table.insert(items, { id = book.id, text = book.title, detail = App.describe(book) })
  end

  self.env:pick({ title = "Which book is this?", items = items }, function(picked)
    for _, book in ipairs(books) do
      if book.id == picked then
        return self:shelve_and_link(book)
      end
    end
  end)
end

-- The library does not have it, so the reader can put it there: a Book made from what the
-- document says about itself, then shelved and linked like any other.
function App:offer_to_add_by_hand(title, author)
  self.env:prompt({
    text = string.format('Nothing found for "%s". Add it to the library by hand?', title),
    choices = {
      { id = "add", label = "Add it by hand" },
      { id = "again", label = "Search again" },
      { id = "cancel", label = "Cancel" },
    },
  }, function(answer)
    if answer == "add" then
      self:add_by_hand(title, author)
    elseif answer == "again" then
      self:link_by_search(title, author)
    end
  end)
end

function App:add_by_hand(title, author)
  self.env:ask_text({
    title = "Add a book by hand",
    fields = {
      { id = "title", label = "Title", value = title },
      { id = "author", label = "Author(s), separated by ;", value = author },
      { id = "pages", label = "Pages (optional)", value = "" },
    },
    submit = "Add",
  }, function(values)
    if not values then
      return
    end

    local book, err = self.client:add_book({
      title = values.title,
      authors = App.split_authors(values.author),
      totalPages = tonumber(values.pages),
    })
    if err then
      return self:complain(err)
    end

    self:shelve_and_link(book)
  end)
end

-- "Author · 304 pages", or just the author when the length is unknown.
function App.describe(book)
  local authors = table.concat(book.authors or {}, ", ")
  if book.totalPages and book.totalPages > 0 then
    return string.format("%s · %d pages", authors, book.totalPages)
  end
  return authors
end

function App.split_authors(text)
  local authors = {}
  for author in tostring(text or ""):gmatch("[^;\n]+") do
    local trimmed = author:gsub("^%s+", ""):gsub("%s+$", "")
    if trimmed ~= "" then
      table.insert(authors, trimmed)
    end
  end
  return authors
end

function App:document_title()
  local document = self.env.document or {}
  if document.title and document.title ~= "" then
    return document.title
  end
  -- Sideloaded files often carry no metadata at all; the file name is what the reader knows it by.
  local name = tostring(document.file or ""):match("([^/\\]+)$") or ""
  return (name:gsub("%.[^.]+$", ""):gsub("_", " "))
end

-- The first author only: a search by "A; B; C" matches nothing, and one name is enough.
function App:document_author()
  local authors = (self.env.document or {}).authors
  if type(authors) == "table" then
    authors = authors[1]
  end
  return App.split_authors(authors)[1] or ""
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
