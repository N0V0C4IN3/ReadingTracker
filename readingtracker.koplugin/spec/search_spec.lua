local FakeEnv = require("spec.support.fake_env")
local App = require("readingtracker.app")

local BOOK_ID = "11111111-1111-1111-1111-111111111111"
local OTHER_BOOK_ID = "33333333-3333-3333-3333-333333333333"
local ENTRY_ID = "22222222-2222-2222-2222-222222222222"

local function search_finds(env, books)
  env:respond("GET", "/api/books/search?title=", 200, { results = books, page = 1, pageSize = 20, hasMore = false })
end

local RESULTS = {
  { id = BOOK_ID, title = "The Left Hand of Darkness", authors = { "Ursula K. Le Guin" }, totalPages = 304 },
  { id = OTHER_BOOK_ID, title = "The Left Hand of Darkness (Audio)", authors = { "Ursula K. Le Guin" }, totalPages = nil },
}

describe("linking a document without a usable ISBN", function()
  local function open_without_isbn(...)
    local env = FakeEnv.new({ identifiers = "calibre:42", title = "The Left Hand of Darkness", authors = "Ursula K. Le Guin" })
    env:respond("GET", "/api/library", 200, {})
    env:respond("POST", "/api/library", 201, { id = ENTRY_ID, bookId = BOOK_ID, status = "WantToRead" })
    for _, answer in ipairs({ ... }) do
      env:answer(answer)
    end
    local app = App.new(env)
    return env, app
  end

  it("asks once whether to search", function()
    local env, app = open_without_isbn(nil)

    app:onReaderReady()

    local prompt = env:last_prompt()
    assert.are.equal("prompt", prompt.kind)
    assert.are.equal('Link "The Left Hand of Darkness" to ReadingTracker?', prompt.text)
    assert.are.same({ "search", "not_now", "never" }, { prompt.choices[1].id, prompt.choices[2].id, prompt.choices[3].id })
  end)

  it("asks the same when the catalog does not know the ISBN it has", function()
    local env = FakeEnv.new({ identifiers = "isbn:9780441478125", title = "The Left Hand of Darkness" })
    env:respond("GET", "/api/books/search?isbn=", 200, { results = {}, page = 1, pageSize = 20, hasMore = false })
    local app = App.new(env)

    app:onReaderReady()

    assert.are.equal('Link "The Left Hand of Darkness" to ReadingTracker?', env:last_prompt().text)
  end)

  it("searches with the title and author filled in, and links what is picked", function()
    local env, app = open_without_isbn("search", { title = "The Left Hand of Darkness", author = "Ursula K. Le Guin" }, BOOK_ID)
    search_finds(env, RESULTS)

    app:onReaderReady()

    local form = env.prompts[2]
    assert.are.equal("form", form.kind)
    assert.are.same({ "The Left Hand of Darkness", "Ursula K. Le Guin" }, { form.fields[1].value, form.fields[2].value })

    local search = env:requests_to("GET", "/api/books/search")[1]
    assert.matches("title=The%20Left%20Hand%20of%20Darkness", search.url, 1, true)
    assert.matches("author=Ursula%20K.%20Le%20Guin", search.url, 1, true)

    local list = env.prompts[3]
    assert.are.equal("pick", list.kind)
    assert.are.equal(2, #list.items)
    assert.are.equal(BOOK_ID, list.items[1].id)
    assert.are.equal("The Left Hand of Darkness", list.items[1].text)
    assert.are.equal("Ursula K. Le Guin · 304 pages", list.items[1].detail)
    assert.are.equal("Ursula K. Le Guin", list.items[2].detail)

    assert.are.equal(1, #env:requests_to("POST", "/api/library"))
    assert.are.equal(ENTRY_ID, env:read_setting("entry_id"))
    assert.are.same({ "Linked to The Left Hand of Darkness" }, env.notices)
  end)

  it("links without adding when the picked book is already on the shelf", function()
    local env, app = open_without_isbn("search", { title = "Left Hand", author = "" }, BOOK_ID)
    env.responses = {}
    search_finds(env, RESULTS)
    env:respond("GET", "/api/library", 200, { { id = ENTRY_ID, bookId = BOOK_ID, status = "Reading" } })

    app:onReaderReady()

    assert.are.same({}, env:requests_to("POST", "/api/library"))
    assert.are.equal(ENTRY_ID, env:read_setting("entry_id"))
  end)

  it("offers to add the book by hand when the search finds nothing", function()
    local env, app = open_without_isbn("search", { title = "An Obscure Book", author = "Nobody" }, "add", { title = "An Obscure Book", author = "Nobody", pages = "180" })
    search_finds(env, {})
    env:respond("POST", "/api/books", 201, { id = BOOK_ID, title = "An Obscure Book", authors = { "Nobody" }, totalPages = 180 })

    app:onReaderReady()

    local offer = env.prompts[3]
    assert.are.equal("prompt", offer.kind)
    assert.matches("Nothing found", offer.text, 1, true)
    assert.are.equal("add", offer.choices[1].id)

    local form = env.prompts[4]
    assert.are.equal("form", form.kind)
    assert.are.same({ "An Obscure Book", "Nobody" }, { form.fields[1].value, form.fields[2].value })

    local created = env:requests_to("POST", "/api/books")[1]
    assert.matches('"title":"An Obscure Book"', created.body, 1, true)
    assert.matches('"authors":["Nobody"]', created.body, 1, true)
    assert.matches('"totalPages":180', created.body, 1, true)
    assert.are.equal(1, #env:requests_to("POST", "/api/library"))
    assert.are.equal(ENTRY_ID, env:read_setting("entry_id"))
  end)

  it("says to wait a minute when the search is rate limited", function()
    local env, app = open_without_isbn("search", { title = "The Left Hand of Darkness", author = "" })
    env:respond("GET", "/api/books/search?title=", 429, "")

    app:onReaderReady()

    assert.are.same({ "ReadingTracker asked this device to wait a minute." }, env.notices)
    assert.is_nil(env:read_setting("entry_id"))
  end)

  it("does not ask while offline, since a search could go nowhere", function()
    local env = FakeEnv.new({ identifiers = "calibre:42", online = false })
    local app = App.new(env)

    app:onReaderReady()

    assert.are.same({}, env.prompts)
    assert.is_nil(env:read_setting("never"))
  end)

  it("remembers never", function()
    local env, app = open_without_isbn("never")

    app:onReaderReady()

    assert.is_true(env:read_setting("never"))
    app:onReaderReady()
    assert.are.equal(1, #env.prompts)
  end)

  it("asks again next opening after not now", function()
    local env, app = open_without_isbn("not_now")

    app:onReaderReady()
    app:onReaderReady()

    assert.are.equal(2, #env.prompts)
  end)

  it("re-links from the menu, replacing the link", function()
    local env, app = open_without_isbn({ title = "Left Hand", author = "" }, OTHER_BOOK_ID)
    env:save_setting("entry_id", ENTRY_ID)
    env:save_setting("entry_title", "Something Else")
    env.responses = {}
    search_finds(env, RESULTS)
    env:respond("GET", "/api/library", 200, {})
    env:respond("POST", "/api/library", 201, { id = "44444444-4444-4444-4444-444444444444", bookId = OTHER_BOOK_ID, status = "WantToRead" })

    app:link_by_search()

    -- Straight to the form: the reader asked for this, so there is nothing to confirm first.
    assert.are.equal("form", env.prompts[1].kind)
    assert.are.equal("44444444-4444-4444-4444-444444444444", env:read_setting("entry_id"))
    assert.are.equal("The Left Hand of Darkness (Audio)", env:read_setting("entry_title"))
  end)
end)
