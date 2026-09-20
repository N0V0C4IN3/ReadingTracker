local json = require("readingtracker.vendor.json")
local FakeEnv = require("spec.support.fake_env")
local App = require("readingtracker.app")

local ENTRY_ID = "22222222-2222-2222-2222-222222222222"

local function open_linked(status, options)
  options = options or {}
  local env = FakeEnv.new({ page_count = 200, page = 1, online = options.online })
  env:save_setting("entry_id", ENTRY_ID)
  env:save_setting("entry_title", "A Linked Book")
  env:respond("PUT", "/bookmark", 200, {
    session = { id = "s1", amount = 10, unit = "Percentage", source = "Device" },
    entry = { id = ENTRY_ID, status = status, bookmark = { percent = 100, reportedAt = "2026-09-18T12:00:00+00:00" } },
  })
  env:respond("PUT", "/status", 200, { id = ENTRY_ID, status = "Finished" })
  local app = App.new(env)
  app:onReaderReady()
  return env, app
end

local function reach_the_end(env, app)
  env:advance(60)
  env:turn_to(200)
  app:onPageUpdate(200)
  app:onEndOfBook()
end

local function status_changes(env)
  local sent = {}
  for _, request in ipairs(env:requests_to("PUT", "/status")) do
    table.insert(sent, json.decode(request.body).status)
  end
  return sent
end

describe("finishing a book from the device", function()
  it("asks at the end of a book the reader has not finished, and marks it finished on yes", function()
    local env, app = open_linked("Reading")
    env:answer("yes")

    reach_the_end(env, app)

    -- The end is reported first, so the shelf shows 100% whatever the answer.
    assert.are.equal(1, #env:requests_to("PUT", "/bookmark"))
    local prompt = env:last_prompt()
    assert.are.equal("That's all of A Linked Book — mark it as finished?", prompt.text)
    assert.are.same({ "Finished" }, status_changes(env))
    assert.are.same({ "Marked as finished" }, env.notices)
  end)

  it("does nothing more on no, and does not ask again until the book is reopened", function()
    local env, app = open_linked("Reading")
    env:answer("no")

    reach_the_end(env, app)
    app:onEndOfBook()

    assert.are.same({}, status_changes(env))
    assert.are.equal(1, #env.prompts)

    app:onCloseDocument()
    app:onReaderReady()
    env:answer("no")
    reach_the_end(env, app)
    assert.are.equal(2, #env.prompts)
  end)

  it("never asks about a book that is already finished, but still reports the reading", function()
    local env, app = open_linked("Finished")

    reach_the_end(env, app)

    assert.are.same({}, env.prompts)
    assert.are.equal(1, #env:requests_to("PUT", "/bookmark"))
  end)

  it("does not ask while offline, since the answer could go nowhere", function()
    local env, app = open_linked("Reading", { online = false })

    reach_the_end(env, app)

    assert.are.same({}, env.prompts)
    assert.are.equal(100, env:read_setting("pending").percent)
  end)

  it("marks a book finished from the menu", function()
    local env, app = open_linked("Reading")

    app:mark_finished()

    assert.are.same({ "Finished" }, status_changes(env))
    assert.are.same({ "Marked as finished" }, env.notices)
  end)

  it("says so when marking finished cannot get through", function()
    local env, app = open_linked("Reading")
    env.responses = {}
    env:respond("PUT", "/status", 503, "")

    app:mark_finished()

    assert.are.same({ "ReadingTracker could not be reached just now." }, env.notices)
  end)
end)
