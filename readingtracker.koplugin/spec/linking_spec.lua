local FakeEnv = require("spec.support.fake_env")
local App = require("readingtracker.app")

-- The Catalog's Book for an ISBN, and the reader's shelf, as the Gateway answers them.
local BOOK_ID = "11111111-1111-1111-1111-111111111111"
local ENTRY_ID = "22222222-2222-2222-2222-222222222222"

local function catalog_has(env, isbn)
  env:respond("GET", "/api/books/search?isbn=" .. isbn, 200, {
    books = { { id = BOOK_ID, title = "The Left Hand of Darkness", authors = { "Ursula K. Le Guin" }, totalPages = 304 } },
    page = 1, pageSize = 20, hasMore = false,
  })
end

local function catalog_has_nothing(env, isbn)
  env:respond("GET", "/api/books/search?isbn=" .. isbn, 200, { books = {}, page = 1, pageSize = 20, hasMore = false })
end

local function shelf_is_empty(env)
  env:respond("GET", "/api/library", 200, {})
end

local function shelf_has_the_book(env)
  env:respond("GET", "/api/library", 200, { { id = ENTRY_ID, bookId = BOOK_ID, status = "Reading" } })
end

describe("linking a document by ISBN", function()
  it("links a book that is already on the shelf without asking", function()
    local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
    catalog_has(env, "9780441478125")
    shelf_has_the_book(env)
    local app = App.new(env)

    app:onReaderReady()

    assert.are.equal(ENTRY_ID, env:read_setting("entry_id"))
    assert.are.same({}, env.prompts)
    assert.are.same({ "Linked to The Left Hand of Darkness" }, env.notices)
  end)

  it("does nothing more once the document is linked", function()
    local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
    env:save_setting("entry_id", ENTRY_ID)
    local app = App.new(env)

    app:onReaderReady()

    assert.are.same({}, env.requests)
  end)

  describe("a book the reader does not have yet", function()
    local function open_unshelved_book(answer)
      local env = FakeEnv.new({ identifiers = "urn:isbn:978-0-441-47812-5" })
      catalog_has(env, "9780441478125")
      shelf_is_empty(env)
      env:respond("POST", "/api/library", 201, { id = ENTRY_ID, bookId = BOOK_ID, status = "WantToRead" })
      env:answer(answer)
      local app = App.new(env)
      app:onReaderReady()
      return env, app
    end

    it("asks before putting it on the shelf", function()
      local env = open_unshelved_book(nil)

      local prompt = env:last_prompt()
      assert.are.equal('Add "The Left Hand of Darkness" to your ReadingTracker library and sync progress?', prompt.text)
      assert.are.same({ "yes", "not_now", "never" }, { prompt.choices[1].id, prompt.choices[2].id, prompt.choices[3].id })
    end)

    it("adds it and links on yes", function()
      local env = open_unshelved_book("yes")

      local added = env:requests_to("POST", "/api/library")
      assert.are.equal(1, #added)
      assert.matches('"bookId":"' .. BOOK_ID .. '"', added[1].body, 1, true)
      assert.are.equal(ENTRY_ID, env:read_setting("entry_id"))
      assert.are.same({ "Linked to The Left Hand of Darkness" }, env.notices)
    end)

    it("remembers nothing on not now, so it asks again next time", function()
      local env, app = open_unshelved_book("not_now")

      assert.are.same({}, env:requests_to("POST", "/api/library"))
      assert.is_nil(env:read_setting("entry_id"))
      assert.is_nil(env:read_setting("never"))

      app:onReaderReady()
      assert.are.equal(2, #env.prompts)
    end)

    it("remembers never, and never asks again", function()
      local env, app = open_unshelved_book("never")

      assert.is_true(env:read_setting("never"))
      assert.is_nil(env:read_setting("entry_id"))

      env.requests = {}
      app:onReaderReady()
      assert.are.same({}, env.requests)
      assert.are.equal(1, #env.prompts)
    end)

    it("links a document the catalog knows but the ISBN was hyphenated in", function()
      local env = open_unshelved_book("yes")

      assert.are.equal(1, #env:requests_to("GET", "isbn=9780441478125"))
    end)
  end)

  it("does nothing for a document with no ISBN", function()
    local env = FakeEnv.new({ identifiers = "calibre:42\nuuid:0d6b2c7e" })
    local app = App.new(env)

    app:onReaderReady()

    assert.are.same({}, env.requests)
    assert.are.same({}, env.prompts)
  end)

  it("does nothing when the catalog does not know the ISBN", function()
    local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
    catalog_has_nothing(env, "9780441478125")
    local app = App.new(env)

    app:onReaderReady()

    assert.are.same({}, env.prompts)
    assert.is_nil(env:read_setting("entry_id"))
  end)

  it("stays quiet when offline and tries again next opening", function()
    local env = FakeEnv.new({ identifiers = "isbn:9780441478125", online = false })
    local app = App.new(env)

    app:onReaderReady()

    assert.are.same({}, env.notices)
    assert.are.same({}, env.prompts)
    assert.is_nil(env:read_setting("never"))
  end)

  it("says the token was refused, once, naming the config file, and stops", function()
    local env = FakeEnv.new({ identifiers = "isbn:9780441478125" })
    env:respond("GET", "/api/books/search", 401, "")
    local app = App.new(env)

    app:onReaderReady()
    app:onReaderReady()

    assert.are.equal(1, #env.notices)
    assert.matches("readingtracker_config.lua", env.notices[1], 1, true)
    assert.are.equal(1, #env.requests)
  end)

  it("unlinks from the menu", function()
    local env = FakeEnv.new({})
    env:save_setting("entry_id", ENTRY_ID)
    env:save_setting("entry_title", "The Left Hand of Darkness")
    local app = App.new(env)

    app:unlink()

    assert.is_nil(env:read_setting("entry_id"))
    assert.is_nil(env:read_setting("entry_title"))
    assert.is_true(env.flushed > 0)
  end)
end)
