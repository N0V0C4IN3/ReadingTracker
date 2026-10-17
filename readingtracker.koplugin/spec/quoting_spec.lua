local json = require("readingtracker.vendor.json")
local FakeEnv = require("spec.support.fake_env")
local App = require("readingtracker.app")

local BOOK_ID = "11111111-1111-1111-1111-111111111111"
local ENTRY_ID = "22222222-2222-2222-2222-222222222222"
local QUOTES = "/api/library/" .. ENTRY_ID .. "/quotes/"
local NOT_KEPT = "The Quote was not kept: this document is not linked to a book on your shelf."
local OFFLINE = "Offline — the Quote is kept and sent at the next chance."

-- A highlight as KOReader keeps it, beginning on page 150 of 300.
local function highlight(overrides)
  local made = {
    datetime = "2026-10-17 09:41:07",
    start = "/body/DocFragment[12]/body/p[3]/text().14",
    passage = "Light is the left hand of darkness.",
    note = nil,
    page = 150,
    chapter = "The Ice",
  }
  for key, value in pairs(overrides or {}) do
    made[key] = value
  end
  return made
end

-- Library's answer to a save: 201 for a Quote it did not have, 200 for one it updated.
local function library_answers(env, status)
  env.responses = {}
  env:respond("PUT", QUOTES, status, { id = "33333333-3333-3333-3333-333333333333" })
end

local function linked(options)
  local env = FakeEnv.new(options)
  env:save_setting("entry_id", ENTRY_ID)
  env:save_setting("entry_title", "The Left Hand of Darkness")
  return env, App.new(env)
end

local function saves(env)
  local sent = {}
  for _, request in ipairs(env:requests_to("PUT", QUOTES)) do
    table.insert(sent, { identity = request.url:match("/quotes/(.+)$"), body = json.decode(request.body) })
  end
  return sent
end

-- How many are still waiting: a later chance, online, sends them.
local function still_kept(env, app)
  local before = #saves(env)
  env.online = true
  library_answers(env, 201)
  app:onNetworkConnected()
  return #saves(env) - before
end

describe("saving a highlight on a linked document", function()
  it("sends the passage, note, position, chapter and save time, and says Saved", function()
    local env, app = linked()
    library_answers(env, 201)

    app:save_quote(highlight({ note = "The book's own title." }))

    local sent = saves(env)
    assert.are.equal(1, #sent)
    assert.are.same({
      passage = "Light is the left hand of darkness.",
      note = "The book's own title.",
      percent = 50,
      chapter = "The Ice",
      savedAt = App.iso(env.now),
    }, sent[1].body)
    assert.are.same({ "Saved to ReadingTracker" }, env.notices)
  end)

  it("names the highlight by when KOReader made it, in letters, digits and dashes", function()
    local env, app = linked()
    library_answers(env, 201)

    app:save_quote(highlight())

    assert.are.equal("ko-20261017094107", saves(env)[1].identity)
  end)

  it("sends no colour or style, and no note when there is none", function()
    local env, app = linked()
    library_answers(env, 201)

    app:save_quote(highlight({ note = "  ", color = "yellow", drawer = "lighten" }))

    local body = saves(env)[1].body
    assert.is_nil(body.note)
    assert.is_nil(body.color)
    assert.is_nil(body.drawer)
  end)

  it("says Updated when Library already had it, sending the same identity with the new note", function()
    local env, app = linked()
    library_answers(env, 201)
    app:save_quote(highlight())

    library_answers(env, 200)
    app:save_quote(highlight({ note = "Read this again." }))

    local sent = saves(env)
    assert.are.equal(sent[1].identity, sent[2].identity)
    assert.are.equal("Read this again.", sent[2].body.note)
    assert.are.same({ "Saved to ReadingTracker", "Updated in ReadingTracker" }, env.notices)
  end)

  it("says what Library says, not what the device remembers", function()
    -- Saved before, then deleted on the web: Library makes it again.
    local env, app = linked()
    library_answers(env, 201)
    app:save_quote(highlight())
    app:save_quote(highlight())

    assert.are.same({ "Saved to ReadingTracker", "Saved to ReadingTracker" }, env.notices)
  end)

  it("names a different highlight of the same passage differently", function()
    local env, app = linked()
    library_answers(env, 201)

    app:save_quote(highlight())
    app:save_quote(highlight({ datetime = "2026-10-17 10:02:55" }))

    local sent = saves(env)
    assert.are_not.equal(sent[1].identity, sent[2].identity)
  end)

  it("keeps a highlight's name when it is extended or its edges are moved, so it stays one Quote", function()
    local env, app = linked()
    library_answers(env, 201)

    app:save_quote(highlight())
    app:save_quote(highlight({
      start = "/body/DocFragment[12]/body/p[2]/text().40",
      passage = "And so: Light is the left hand of darkness.",
    }))
    app:save_quote(highlight({ start = { page = 12, x = 105.5, y = 330.25 } }))
    app:save_quote(highlight({ start = { page = 12, x = 98, y = 330.25 } }))

    local sent = saves(env)
    assert.are.equal(sent[1].identity, sent[2].identity)
    assert.are.equal(sent[3].identity, sent[4].identity)
  end)

  it("names a highlight with no creation time by where it begins", function()
    local env, app = linked()
    library_answers(env, 201)

    app:save_quote(highlight({ datetime = false, start = { page = 12, x = 105.5, y = 330.25 } }))
    app:save_quote(highlight({ datetime = false, start = { page = 12, x = 105.5, y = 330.25 } }))
    app:save_quote(highlight({ datetime = false, start = { page = 13, x = 105.5, y = 330.25 } }))

    local sent = saves(env)
    assert.matches("^ko%-at%-%x%x%x%x%x%x%x%x$", sent[1].identity)
    assert.are.equal(sent[1].identity, sent[2].identity)
    assert.are_not.equal(sent[1].identity, sent[3].identity)
  end)

  it("sends no chapter for a document without a table of contents", function()
    local env, app = linked()
    library_answers(env, 201)

    app:save_quote(highlight({ chapter = "" }))

    assert.is_nil(saves(env)[1].body.chapter)
  end)

  it("keeps nothing for a highlight with no text", function()
    local env, app = linked()

    app:save_quote(highlight({ passage = "   " }))

    assert.are.equal(0, #env.requests)
    assert.are.same({ "There is no text in this highlight to keep." }, env.notices)
  end)

  it("keeps nothing for a highlight KOReader cannot place, rather than putting it at 0%", function()
    local env, app = linked()

    app:save_quote(highlight({ page = false }))

    assert.are.equal(0, #env.requests)
    assert.are.same({ "KOReader did not say where this highlight is, so it was not kept." }, env.notices)
  end)

  it("tells the Gateway's refusal plainly, and does not keep it", function()
    local env, app = linked()
    env:respond("PUT", QUOTES, 400, { errors = { passage = { "A passage is at most 20000 characters." } } })

    app:save_quote(highlight())

    assert.are.same({ "ReadingTracker said no: A passage is at most 20000 characters." }, env.notices)
    assert.are.equal(0, still_kept(env, app))
  end)

  it("keeps it when the book has gone from the shelf, until the document is linked again", function()
    local env, app = linked()
    env:respond("PUT", QUOTES, 404, "")

    app:save_quote(highlight())

    assert.are.same({ "The book this document is linked to is no longer on your shelf. Quotes are kept: link the document again from the menu and they are sent." }, env.notices)
    assert.are.equal(1, still_kept(env, app))
  end)

  it("sends the newer note, not a kept older one, when saved again once the Gateway answers", function()
    local env, app = linked()
    env:respond("PUT", QUOTES, 503, "")
    app:save_quote(highlight({ note = "old" }))

    library_answers(env, 200)
    app:save_quote(highlight({ note = "new" }))
    app:onNetworkConnected()

    local sent = saves(env)
    assert.are.same({ "old", "new" }, { sent[1].body.note, sent[2].body.note })
    assert.are.equal(2, #sent)
  end)

  it("sends Quotes kept from before once a position gets through, without waiting to reconnect", function()
    local env, app = linked({ online = false, page_count = 300 })
    app:onReaderReady()
    app:save_quote(highlight())

    -- Wifi comes up with no reconnection seen, and the next report gets through.
    env.online = true
    library_answers(env, 201)
    env:respond("PUT", "/bookmark", 200, { entry = { id = ENTRY_ID, status = "Reading" } })
    env:advance(60)
    env:turn_to(20)
    app:onPageUpdate(20)
    app:onCloseDocument()

    assert.are.equal(1, #saves(env))
  end)

  it("tries a Quote with an answer it cannot read a few times, sends the rest meanwhile, then drops it", function()
    local env, app = linked({ online = false })
    app:save_quote(highlight({ datetime = "2026-10-17 08:00:00" }))
    app:save_quote(highlight({ datetime = "2026-10-17 08:30:00" }))
    env.online = true
    function env:send(request)
      table.insert(self.requests, request)
      if request.url:find("080000", 1, true) then
        return 500, ""
      end
      return 201, json.encode({ id = "x" })
    end

    for _ = 1, 5 do
      app:onNetworkConnected()
    end

    local tries = 0
    for _, request in ipairs(env:requests_to("PUT", QUOTES)) do
      if request.url:find("080000", 1, true) then
        tries = tries + 1
      end
    end
    assert.are.equal(5, tries)
    assert.are.equal("A Quote could not be sent after 5 tries and was dropped.", env.notices[#env.notices])
    assert.are.equal(0, still_kept(env, app))
  end)

  it("says it is kept when the Gateway cannot answer just now", function()
    local env, app = linked()
    env:respond("PUT", QUOTES, 503, "")

    app:save_quote(highlight())

    assert.are.same({ "ReadingTracker could not be reached just now. Quotes are kept and sent at the next chance." }, env.notices)
    assert.are.equal(1, still_kept(env, app))
  end)
end)

describe("saving a highlight while offline", function()
  it("keeps it, never asks for wifi, and sends it at the next chance with its own time", function()
    local env, app = linked({ online = false })
    app:save_quote(highlight())
    local saved_at = App.iso(env.now)

    assert.are.equal(0, #env.requests)
    assert.are.same({ OFFLINE }, env.notices)

    env:advance(3 * 60 * 60)
    env.online = true
    library_answers(env, 201)
    app:onNetworkConnected()

    local sent = saves(env)
    assert.are.equal(1, #sent)
    assert.are.equal(saved_at, sent[1].body.savedAt)
    assert.are.equal("Sent a kept Quote to ReadingTracker", env.notices[#env.notices])
    assert.are.equal(0, still_kept(env, app))
  end)

  it("sends what was kept when the document is opened again online", function()
    local env, app = linked({ online = false })
    app:save_quote(highlight())
    app:save_quote(highlight({ datetime = "2026-10-17 11:00:00" }))

    env.online = true
    library_answers(env, 201)
    App.new(env):onReaderReady()

    assert.are.equal(2, #saves(env))
    assert.are.equal("Sent 2 kept Quotes to ReadingTracker", env.notices[#env.notices])
  end)

  it("keeps only the latest save of the same highlight", function()
    local env, app = linked({ online = false })
    app:save_quote(highlight({ note = "first" }))
    app:save_quote(highlight({ note = "second" }))

    env.online = true
    library_answers(env, 201)
    app:onNetworkConnected()

    local sent = saves(env)
    assert.are.equal(1, #sent)
    assert.are.equal("second", sent[1].body.note)
  end)

  it("sends the kept ones first, in order, when the next is saved online", function()
    local env, app = linked({ online = false })
    app:save_quote(highlight({ datetime = "2026-10-17 08:00:00" }))
    app:save_quote(highlight({ datetime = "2026-10-17 08:30:00" }))

    env.online = true
    library_answers(env, 201)
    app:save_quote(highlight({ datetime = "2026-10-17 09:00:00" }))

    local sent = saves(env)
    assert.matches("^ko%-20261017080000", sent[1].identity)
    assert.matches("^ko%-20261017083000", sent[2].identity)
    assert.matches("^ko%-20261017090000", sent[3].identity)
    assert.are.same({ "Sent 2 kept Quotes to ReadingTracker", "Saved to ReadingTracker" }, { env.notices[3], env.notices[4] })
  end)

  it("says once, not once per Quote, when the book has gone from the shelf", function()
    local env, app = linked({ online = false })
    app:save_quote(highlight({ datetime = "2026-10-17 08:00:00" }))
    app:save_quote(highlight({ datetime = "2026-10-17 08:30:00" }))
    env.notices = {}

    env.online = true
    env:respond("PUT", QUOTES, 404, "")
    app:onNetworkConnected()

    assert.are.equal(1, #saves(env))
    assert.are.equal(1, #env.notices)
  end)

  it("drops a kept Quote the Gateway refuses, says why, and sends the rest", function()
    local env, app = linked({ online = false })
    app:save_quote(highlight({ datetime = "2026-10-17 08:00:00" }))
    app:save_quote(highlight({ datetime = "2026-10-17 08:30:00" }))
    env.online = true
    local answers = { 400, 201 }
    function env:send(request)
      table.insert(self.requests, request)
      if table.remove(answers, 1) == 400 then
        return 400, json.encode({ errors = { percent = { "Say where in the book the passage is, between 0 and 100 percent." } } })
      end
      return 201, json.encode({ id = "x" })
    end

    app:onNetworkConnected()

    assert.are.same({
      "ReadingTracker said no: Say where in the book the passage is, between 0 and 100 percent.",
      "Sent a kept Quote to ReadingTracker",
    }, { env.notices[#env.notices - 1], env.notices[#env.notices] })
    assert.are.equal(0, still_kept(env, app))
  end)
end)

describe("saving a highlight on a document that is not linked", function()
  local function catalog_has(env, isbn)
    env:respond("GET", "/api/books/search?isbn=" .. isbn, 200, {
      results = { { id = BOOK_ID, title = "The Left Hand of Darkness", authors = { "Ursula K. Le Guin" } } },
    })
  end

  it("links a book with an ISBN that is on the shelf, then saves to it", function()
    local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
    catalog_has(env, "9780441478125")
    env:respond("GET", "/api/library", 200, { { id = ENTRY_ID, bookId = BOOK_ID, status = "Reading" } })
    env:respond("PUT", QUOTES, 201, { id = "q" })

    App.new(env):save_quote(highlight())

    assert.are.equal(ENTRY_ID, env:read_setting("entry_id"))
    assert.are.equal(1, #saves(env))
    assert.are.same({ "Linked to The Left Hand of Darkness", "Saved to ReadingTracker" }, env.notices)
  end)

  it("adds a book with an ISBN to the shelf on yes, then saves to it", function()
    local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
    catalog_has(env, "9780441478125")
    env:respond("GET", "/api/library", 200, {})
    env:respond("POST", "/api/library", 201, { id = ENTRY_ID, bookId = BOOK_ID, status = "WantToRead" })
    env:respond("PUT", QUOTES, 201, { id = "q" })
    env:answer("yes")

    App.new(env):save_quote(highlight())

    assert.are.equal(1, #saves(env))
    assert.are.equal("Saved to ReadingTracker", env.notices[#env.notices])
  end)

  it("searches for a book with no ISBN, then saves to the one picked", function()
    local env = FakeEnv.new()
    env:respond("GET", "/api/books/search?title=", 200, {
      results = { { id = BOOK_ID, title = "The Left Hand of Darkness", authors = { "Ursula K. Le Guin" } } },
    })
    env:respond("GET", "/api/library", 200, {})
    env:respond("POST", "/api/library", 201, { id = ENTRY_ID, bookId = BOOK_ID, status = "WantToRead" })
    env:respond("PUT", QUOTES, 201, { id = "q" })
    env:answer("search")
    env:answer({ title = "The Left Hand of Darkness", author = "Le Guin" })
    env:answer(BOOK_ID)

    App.new(env):save_quote(highlight())

    assert.are.equal(ENTRY_ID, env:read_setting("entry_id"))
    assert.are.equal(1, #saves(env))
    assert.are.equal("Saved to ReadingTracker", env.notices[#env.notices])
  end)

  it("adds a book by hand when the search finds nothing, then saves to it", function()
    local env = FakeEnv.new()
    env:respond("GET", "/api/books/search?title=", 200, { results = {} })
    env:respond("POST", "/api/books", 201, { id = BOOK_ID, title = "A Sideloaded Book", authors = { "Someone" } })
    env:respond("GET", "/api/library", 200, {})
    env:respond("POST", "/api/library", 201, { id = ENTRY_ID, bookId = BOOK_ID, status = "WantToRead" })
    env:respond("PUT", QUOTES, 201, { id = "q" })
    env:answer("search")
    env:answer({ title = "A Sideloaded Book", author = "Someone" })
    env:answer("add")
    env:answer({ title = "A Sideloaded Book", author = "Someone", pages = "" })

    App.new(env):save_quote(highlight())

    assert.are.equal(1, #saves(env))
    assert.are.equal("Saved to ReadingTracker", env.notices[#env.notices])
  end)

  it("still links a document the reader said never to ask about, since the tap asks", function()
    local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
    env:save_setting("never", true)
    catalog_has(env, "9780441478125")
    env:respond("GET", "/api/library", 200, { { id = ENTRY_ID, bookId = BOOK_ID, status = "Reading" } })
    env:respond("PUT", QUOTES, 201, { id = "q" })

    App.new(env):save_quote(highlight())

    assert.is_nil(env:read_setting("never"))
    assert.are.equal(1, #saves(env))
  end)

  it("is not undone by the opening's question answered after the save's began", function()
    -- Questions stay open until answered, as on the device.
    local env = FakeEnv.new()
    local waiting = {}
    function env:prompt(prompt, on_answer)
      table.insert(self.prompts, prompt)
      table.insert(waiting, on_answer)
    end
    env:respond("GET", "/api/books/search?title=", 200, {
      results = { { id = BOOK_ID, title = "The Left Hand of Darkness", authors = {} } },
    })
    env:respond("GET", "/api/library", 200, {})
    env:respond("POST", "/api/library", 201, { id = ENTRY_ID, bookId = BOOK_ID, status = "WantToRead" })
    env:respond("PUT", QUOTES, 201, { id = "q" })
    local app = App.new(env)

    app:onReaderReady()
    app:save_quote(highlight())
    waiting[1]("not_now")
    env:answer({ title = "The Left Hand of Darkness", author = "" })
    env:answer(BOOK_ID)
    waiting[2]("search")

    assert.are.equal(1, #saves(env))
    assert.are.same({ "Linked to The Left Hand of Darkness", "Saved to ReadingTracker" }, env.notices)
  end)

  describe("backing out of linking", function()
    local function backs_out(env, ...)
      for _, answer in ipairs({ ... }) do
        env:answer(answer)
      end
      local app = App.new(env)
      app:save_quote(highlight())
      assert.are.equal(0, #saves(env))
      assert.is_nil(env:read_setting("entry_id"))
      assert.are.equal(NOT_KEPT, env.notices[#env.notices])
      return app
    end

    it("sends and keeps nothing on not now, and says so", function()
      local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
      catalog_has(env, "9780441478125")
      env:respond("GET", "/api/library", 200, {})
      backs_out(env, "not_now")
    end)

    it("sends and keeps nothing on never, and remembers never", function()
      local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
      catalog_has(env, "9780441478125")
      env:respond("GET", "/api/library", 200, {})
      backs_out(env, "never")
      assert.is_true(env:read_setting("never"))
    end)

    it("sends and keeps nothing when the search prompt is dismissed", function()
      backs_out(FakeEnv.new(), nil)
    end)

    it("sends and keeps nothing when the search form is cancelled", function()
      backs_out(FakeEnv.new(), "search", nil)
    end)

    it("sends and keeps nothing when no book is picked", function()
      local env = FakeEnv.new()
      env:respond("GET", "/api/books/search?title=", 200, {
        results = { { id = BOOK_ID, title = "The Left Hand of Darkness", authors = {} } },
      })
      backs_out(env, "search", { title = "x", author = "" }, nil)
    end)

    it("sends and keeps nothing when adding by hand is cancelled", function()
      local env = FakeEnv.new()
      env:respond("GET", "/api/books/search?title=", 200, { results = {} })
      backs_out(env, "search", { title = "x", author = "" }, "cancel")
    end)

    it("sends and keeps nothing when the library cannot be asked, and says both", function()
      local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
      env:respond("GET", "/api/books/search?isbn=9780441478125", 503, "")
      backs_out(env)
      assert.are.same({ "ReadingTracker could not be reached just now.", NOT_KEPT }, env.notices)
    end)

    it("does not save the Quote when a later opening links the document", function()
      local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
      catalog_has(env, "9780441478125")
      env:respond("GET", "/api/library", 200, {})
      backs_out(env, "not_now")

      env:respond("POST", "/api/library", 201, { id = ENTRY_ID, bookId = BOOK_ID, status = "WantToRead" })
      env:answer("yes")
      App.new(env):onReaderReady()

      assert.are.equal(ENTRY_ID, env:read_setting("entry_id"))
      assert.are.equal(0, #saves(env))
    end)
  end)

  it("says to connect when there is no wifi to link with, and keeps nothing", function()
    local env = FakeEnv.new({ identifiers = "isbn:9780441478125", online = false })

    App.new(env):save_quote(highlight())

    assert.are.equal(0, #env.requests)
    assert.are.same({ "Connect to wifi to link this document to a book, then save the Quote again." }, env.notices)
  end)
end)
