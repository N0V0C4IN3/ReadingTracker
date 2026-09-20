local json = require("readingtracker.vendor.json")
local FakeEnv = require("spec.support.fake_env")
local App = require("readingtracker.app")

local ENTRY_ID = "22222222-2222-2222-2222-222222222222"

local function open_linked(options)
  options = options or {}
  local env = FakeEnv.new({ page_count = 200, page = 1, online = options.online })
  env:save_setting("entry_id", ENTRY_ID)
  env:save_setting("entry_title", "A Linked Book")
  env:respond("GET", "/api/library", 200, { { id = "other", status = "Finished" }, { id = ENTRY_ID, status = options.shelf_status or "Reading" } })
  env:respond("PUT", "/status", 200, { id = ENTRY_ID, status = options.becomes or "OnHold" })
  local app = App.new(env)
  app:onReaderReady()
  return env, app
end

local function status_changes(env)
  local sent = {}
  for _, request in ipairs(env:requests_to("PUT", "/status")) do
    table.insert(sent, json.decode(request.body).status)
  end
  return sent
end

local function labels(prompt)
  local seen = {}
  for _, item in ipairs(prompt.items) do
    table.insert(seen, item.detail and (item.text .. " (" .. item.detail .. ")") or item.text)
  end
  return seen
end

describe("changing the reading status from the device", function()
  it("offers every status with the current one marked, and sets the one picked", function()
    local env, app = open_linked()
    env:answer("OnHold")

    app:change_status()

    local prompt = env:last_prompt()
    assert.are.equal("pick", prompt.kind)
    assert.are.equal("A Linked Book is…", prompt.title)
    assert.are.same({ "Want to Read", "Reading (now)", "On Hold", "Dropped", "Finished" }, labels(prompt))
    assert.are.same({ "OnHold" }, status_changes(env))
    assert.are.same({ "Status: On Hold" }, env.notices)
  end)

  it("learns the current status from the shelf only once", function()
    local env, app = open_linked()
    env:answer("OnHold")
    app:change_status()
    env:answer("Reading")

    app:change_status()

    assert.are.equal(1, #env:requests_to("GET", "/api/library"))
    assert.are.same({ "Want to Read", "Reading", "On Hold (now)", "Dropped", "Finished" }, labels(env:last_prompt()))
  end)

  it("sends nothing when the reader picks the status the book already has, or nothing at all", function()
    local env, app = open_linked()
    env:answer("Reading")
    app:change_status()
    env:answer(nil)
    app:change_status()

    assert.are.same({}, status_changes(env))
  end)

  it("says so when offline instead of offering a list that could go nowhere", function()
    local env, app = open_linked({ online = false })

    app:change_status()

    assert.are.same({}, env.prompts)
    assert.are.same({ "Offline — the status can be changed once the device is connected." }, env.notices)
  end)

  it("still offers the list when the shelf cannot be read, just without a mark", function()
    local env, app = open_linked()
    env.responses = {}
    env:respond("GET", "/api/library", 503, "")
    env:respond("PUT", "/status", 200, { id = ENTRY_ID, status = "Dropped" })
    env:answer("Dropped")

    app:change_status()

    assert.are.same({ "Want to Read", "Reading", "On Hold", "Dropped", "Finished" }, labels(env:last_prompt()))
    assert.are.same({ "Dropped" }, status_changes(env))
  end)

  it("wants a linked document", function()
    local env = FakeEnv.new({ page_count = 200, page = 1, online = true })
    local app = App.new(env)

    app:change_status()

    assert.are.same({ "This document is not linked to a book on your shelf." }, env.notices)
  end)
end)
