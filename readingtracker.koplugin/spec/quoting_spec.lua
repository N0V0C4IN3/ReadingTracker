local json = require("readingtracker.vendor.json")
local FakeEnv = require("spec.support.fake_env")
local App = require("readingtracker.app")

local BOOK_ID = "11111111-1111-1111-1111-111111111111"
local ENTRY_ID = "22222222-2222-2222-2222-222222222222"
local QUOTES = "/api/library/" .. ENTRY_ID .. "/quotes/"

-- A highlight as KOReader keeps it, on page 150 of 300.
local function highlight(overrides)
  local made = {
    datetime = "2026-10-17 09:41:07",
    position = "/body/DocFragment[12]/body/p[3]/text().14",
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

local function saves_answered(env, status)
  env:respond("PUT", QUOTES, status or 201, { id = "33333333-3333-3333-3333-333333333333" })
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
    table.insert(sent, { url = request.url, body = json.decode(request.body) })
  end
  return sent
end

describe("saving a highlight on a linked document", function()
  it("sends the passage, note, position, chapter, save time and identity, and says Saved", function()
    local env, app = linked()
    saves_answered(env)

    app:save_quote(highlight({ note = "The book's own title." }))

    local sent = saves(env)
    assert.are.equal(1, #sent)
    assert.are.equal("http://gateway.test/api/library/" .. ENTRY_ID .. "/quotes/" .. App.highlight_identity("2026-10-17 09:41:07", "/body/DocFragment[12]/body/p[3]/text().14"), sent[1].url)
    assert.are.same({
      passage = "Light is the left hand of darkness.",
      note = "The book's own title.",
      percent = 50,
      chapter = "The Ice",
      savedAt = App.iso(env.now),
    }, sent[1].body)
    assert.are.same({ "Saved to ReadingTracker" }, env.notices)
  end)

  it("sends no colour or style, and no note when there is none", function()
    local env, app = linked()
    saves_answered(env)

    app:save_quote(highlight({ note = "  ", color = "yellow", drawer = "lighten" }))

    local body = saves(env)[1].body
    assert.is_nil(body.note)
    assert.is_nil(body.color)
    assert.is_nil(body.drawer)
  end)

  it("says Updated when the same highlight is saved again, with its changed note", function()
    local env, app = linked()
    saves_answered(env)
    app:save_quote(highlight())

    env:advance(600)
    app:save_quote(highlight({ note = "Read this again." }))

    local sent = saves(env)
    assert.are.equal(sent[1].url, sent[2].url)
    assert.are.equal("Read this again.", sent[2].body.note)
    assert.are.same({ "Saved to ReadingTracker", "Updated in ReadingTracker" }, env.notices)
  end)

  it("says Saved for a different highlight of the same passage", function()
    local env, app = linked()
    saves_answered(env)
    app:save_quote(highlight())

    app:save_quote(highlight({ datetime = "2026-10-17 10:02:55" }))

    local sent = saves(env)
    assert.are_not.equal(sent[1].url, sent[2].url)
    assert.are.same({ "Saved to ReadingTracker", "Saved to ReadingTracker" }, env.notices)
  end)

  it("sends no chapter for a document without a table of contents", function()
    local env, app = linked()
    saves_answered(env)

    app:save_quote(highlight({ chapter = "" }))

    assert.is_nil(saves(env)[1].body.chapter)
  end)

  it("keeps nothing and sends nothing for a highlight with no text", function()
    local env, app = linked()

    app:save_quote(highlight({ passage = "   " }))

    assert.are.equal(0, #env.requests)
    assert.are.same({ "There is no text in this highlight to keep." }, env.notices)
  end)

  it("tells the Gateway's refusal plainly", function()
    local env, app = linked()
    env:respond("PUT", QUOTES, 400, { errors = { passage = { "A passage is at most 20000 characters." } } })

    app:save_quote(highlight())

    assert.are.same({ "ReadingTracker said no: A passage is at most 20000 characters." }, env.notices)
    assert.is_nil(env:read_setting(App.QUOTES_KEPT))
  end)

  it("says the book has gone from the shelf when the entry is not there", function()
    local env, app = linked()
    env:respond("PUT", QUOTES, 404, "")

    app:save_quote(highlight())

    assert.are.same({ "The book this document is linked to is no longer on your shelf. Link it again from the menu, then save the Quote again." }, env.notices)
    assert.is_nil(env:read_setting(App.QUOTES_KEPT))
  end)
end)

describe("saving a highlight while offline", function()
  it("keeps it, never asks for wifi, and sends it at the next chance with its own time", function()
    local env, app = linked({ online = false })
    saves_answered(env)
    app:save_quote(highlight())
    local saved_at = App.iso(env.now)

    assert.are.equal(0, #env.requests)
    assert.are.same({ "Offline — the Quote is kept and sent at the next chance." }, env.notices)

    env:advance(3 * 60 * 60)
    env.online = true
    app:onNetworkConnected()

    local sent = saves(env)
    assert.are.equal(1, #sent)
    assert.are.equal(saved_at, sent[1].body.savedAt)
    assert.is_nil(env:read_setting(App.QUOTES_KEPT))
    assert.are.equal("Sent a kept Quote to ReadingTracker", env.notices[#env.notices])
  end)

  it("sends what was kept when the document is opened again online", function()
    local env, app = linked({ online = false })
    saves_answered(env)
    app:save_quote(highlight())
    app:save_quote(highlight({ datetime = "2026-10-17 11:00:00" }))

    env.online = true
    App.new(env):onReaderReady()

    assert.are.equal(2, #saves(env))
    assert.are.equal("Sent 2 kept Quotes to ReadingTracker", env.notices[#env.notices])
  end)

  it("keeps only the latest save of the same highlight", function()
    local env, app = linked({ online = false })
    saves_answered(env)
    app:save_quote(highlight({ note = "first" }))
    app:save_quote(highlight({ note = "second" }))

    env.online = true
    app:onNetworkConnected()

    local sent = saves(env)
    assert.are.equal(1, #sent)
    assert.are.equal("second", sent[1].body.note)
  end)

  it("keeps it when the Gateway cannot answer, and tries again later", function()
    local env, app = linked()
    env:respond("PUT", QUOTES, 503, "")
    app:save_quote(highlight())
    assert.are.equal(1, #env:read_setting(App.QUOTES_KEPT))

    env.responses = {}
    saves_answered(env)
    app:onNetworkConnected()

    assert.is_nil(env:read_setting(App.QUOTES_KEPT))
    assert.are.equal(2, #saves(env))
  end)

  it("drops a kept Quote the Gateway refuses, and says why", function()
    local env, app = linked({ online = false })
    app:save_quote(highlight())
    env.online = true
    env:respond("PUT", QUOTES, 400, { errors = { percent = { "Say where in the book the passage is, between 0 and 100 percent." } } })

    app:onNetworkConnected()

    assert.is_nil(env:read_setting(App.QUOTES_KEPT))
    assert.are.equal("ReadingTracker said no: Say where in the book the passage is, between 0 and 100 percent.", env.notices[#env.notices])
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
    saves_answered(env)

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
    saves_answered(env)
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
    saves_answered(env)
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
    saves_answered(env)
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
    saves_answered(env)

    App.new(env):save_quote(highlight())

    assert.is_nil(env:read_setting("never"))
    assert.are.equal(1, #saves(env))
  end)

  describe("backing out of linking", function()
    local NOT_KEPT = "The Quote was not kept: this document is not linked to a book on your shelf."

    local function backs_out(env, ...)
      for _, answer in ipairs({ ... }) do
        env:answer(answer)
      end
      local app = App.new(env)
      app:save_quote(highlight())
      assert.are.equal(0, #saves(env))
      assert.is_nil(env:read_setting(App.QUOTES_KEPT))
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

    it("does not save a Quote whose linking was left unanswered when the document closed", function()
      local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
      catalog_has(env, "9780441478125")
      env:respond("GET", "/api/library", 200, {})
      -- KOReader closes the question with the document; no answer ever comes.
      local answering = env.prompt
      function env:prompt(prompt) table.insert(self.prompts, prompt) end
      local app = App.new(env)
      app:save_quote(highlight())

      app:onCloseDocument()
      env.prompt = answering
      env:respond("POST", "/api/library", 201, { id = ENTRY_ID, bookId = BOOK_ID, status = "WantToRead" })
      env:answer("yes")
      app:onReaderReady()

      assert.are.equal(ENTRY_ID, env:read_setting("entry_id"))
      assert.are.equal(0, #saves(env))
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
    assert.is_nil(env:read_setting(App.QUOTES_KEPT))
    assert.are.same({ "Connect to wifi to link this document to a book, then save the Quote again." }, env.notices)
  end)
end)

describe("a highlight's identity", function()
  it("is the same for the same highlight every time", function()
    assert.are.equal(App.highlight_identity("2026-10-17 09:41:07", "/body/p[3].14"), App.highlight_identity("2026-10-17 09:41:07", "/body/p[3].14"))
  end)

  it("differs for highlights made in the same second at different places", function()
    assert.are_not.equal(App.highlight_identity("2026-10-17 09:41:07", "/body/p[3].14"), App.highlight_identity("2026-10-17 09:41:07", "/body/p[4].2"))
  end)

  it("is only letters, digits and dashes, as Library asks", function()
    local identity = App.highlight_identity("2026-10-17 09:41:07", "12:105.50:330.25")
    assert.matches("^ko%-20261017094107%-%x%x%x%x%x%x%x%x$", identity)
  end)

  it("still has a form when KOReader kept no time", function()
    assert.matches("^ko%-0%-%x+$", App.highlight_identity(nil, "12:1:1"))
  end)

  it("hashes as FNV-1a", function()
    assert.are.equal("811c9dc5", App.fnv1a(""))
    assert.are.equal("e40c292c", App.fnv1a("a"))
    assert.are.equal("bf9cf968", App.fnv1a("foobar"))
  end)
end)
