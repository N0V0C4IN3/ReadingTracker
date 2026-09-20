local json = require("readingtracker.vendor.json")
local FakeEnv = require("spec.support.fake_env")
local App = require("readingtracker.app")

local ENTRY_ID = "22222222-2222-2222-2222-222222222222"
local NOON = 1789732800 -- 2026-09-18T12:00:00Z

-- A linked document of 200 pages, open at page 1, with the Gateway taking reports.
local function open_linked(options)
  options = options or {}
  local env = FakeEnv.new({ page_count = 200, page = 1, now = NOON, online = options.online, preferences = options.preferences })
  env:save_setting("entry_id", ENTRY_ID)
  env:save_setting("entry_title", "A Linked Book")
  env:respond("PUT", "/api/library/" .. ENTRY_ID .. "/bookmark", 200, {
    session = { id = "s1", amount = 10, unit = "Percentage", source = "Device" },
    entry = { id = ENTRY_ID, status = "Reading", bookmark = { percent = 10, reportedAt = "2026-09-18T12:00:00+00:00" } },
  })
  local app = App.new(env)
  app:onReaderReady()
  return env, app
end

local function reports(env)
  local sent = {}
  for _, request in ipairs(env:requests_to("PUT", "/bookmark")) do
    table.insert(sent, json.decode(request.body))
  end
  return sent
end

-- Turns to a page and moves the clock on, the way reading does.
local function read_to(env, app, page, seconds_later)
  env:advance(seconds_later or 0)
  env:turn_to(page)
  app:onPageUpdate(page)
end

describe("reporting where the reader is", function()
  it("reports the position on close, with the time of the last page turn", function()
    local env, app = open_linked()
    read_to(env, app, 69, 60)
    env:advance(600) -- put down for ten minutes before closing

    app:onCloseDocument()

    local sent = reports(env)
    assert.are.equal(1, #sent)
    assert.are.equal(34.5, sent[1].percent)
    assert.are.equal("2026-09-18T12:01:00Z", sent[1].occurredAt)
  end)

  it("reports on suspend", function()
    local env, app = open_linked()
    read_to(env, app, 20, 30)

    app:onSuspend()

    assert.are.equal(10, reports(env)[1].percent)
  end)

  it("does not report a document that was opened and closed without reading", function()
    local env, app = open_linked()

    app:onCloseDocument()

    assert.are.same({}, reports(env))
  end)

  it("does not report the same position twice", function()
    local env, app = open_linked()
    read_to(env, app, 20, 30)
    app:onSuspend()

    app:onCloseDocument()

    assert.are.equal(1, #reports(env))
  end)

  it("reports while paging once the interval has passed, and not before", function()
    local env, app = open_linked()

    read_to(env, app, 5, 60)
    read_to(env, app, 10, 60)
    assert.are.same({}, reports(env))

    read_to(env, app, 15, 4 * 60) -- six minutes since opening: past the five-minute default
    assert.are.equal(1, #reports(env))
    assert.are.equal(7.5, reports(env)[1].percent)

    read_to(env, app, 20, 60) -- one minute since the last successful report
    assert.are.equal(1, #reports(env))

    read_to(env, app, 30, 5 * 60)
    assert.are.equal(2, #reports(env))
  end)

  it("honours a different interval from the settings", function()
    local env, app = open_linked({ preferences = { interval_minutes = 1 } })

    read_to(env, app, 5, 30)
    assert.are.same({}, reports(env))
    read_to(env, app, 10, 40)
    assert.are.equal(1, #reports(env))
  end)

  it("keeps a report that could not be sent and sends it at the next chance, with its own time", function()
    local env, app = open_linked({ online = false })
    read_to(env, app, 40, 120)
    app:onCloseDocument()

    -- Nothing got through, and nothing was said: an e-reader is offline most of the time.
    assert.are.same({}, reports(env))
    assert.are.same({}, env.notices)
    assert.are.same({ percent = 20, at = NOON + 120 }, env:read_setting("pending"))

    -- Next opening, hours later, with wifi.
    env.online = true
    env:advance(3 * 3600)
    app:onReaderReady()

    local sent = reports(env)
    assert.are.equal(1, #sent)
    assert.are.equal(20, sent[1].percent)
    assert.are.equal("2026-09-18T12:02:00Z", sent[1].occurredAt)
    assert.is_nil(env:read_setting("pending"))
  end)

  it("replaces an older pending position with a newer one", function()
    local env, app = open_linked({ online = false })
    read_to(env, app, 40, 60)
    app:onSuspend()
    read_to(env, app, 60, 60)
    app:onSuspend()

    assert.are.equal(30, env:read_setting("pending").percent)
  end)

  it("sends a pending position when the network comes up", function()
    local env, app = open_linked({ online = false })
    read_to(env, app, 40, 60)
    app:onSuspend()

    env.online = true
    app:onNetworkConnected()

    assert.are.equal(20, reports(env)[1].percent)
    assert.is_nil(env:read_setting("pending"))
  end)

  it("says the token was refused once and sends nothing more this session", function()
    local env, app = open_linked()
    env.responses = {}
    env:respond("PUT", "/bookmark", 401, "")
    read_to(env, app, 40, 6 * 60)
    read_to(env, app, 80, 6 * 60)
    app:onCloseDocument()

    assert.are.equal(1, #reports(env))
    assert.are.equal(1, #env.notices)
    assert.matches("readingtracker_config.lua", env.notices[1], 1, true)
  end)

  it("keeps the position when the gateway asks it to wait", function()
    local env, app = open_linked()
    env.responses = {}
    env:respond("PUT", "/bookmark", 429, "")
    read_to(env, app, 40, 60)

    app:onCloseDocument()

    assert.are.equal(20, env:read_setting("pending").percent)
  end)

  it("syncs now from the menu whatever the interval says", function()
    local env, app = open_linked()
    read_to(env, app, 10, 10)

    app:sync_now()

    assert.are.equal(5, reports(env)[1].percent)
    assert.are.same({ "Reported 5% to ReadingTracker" }, env.notices)
  end)

  it("reports nothing for a document that is not linked", function()
    local env = FakeEnv.new({ page_count = 200, online = true })
    env:save_setting("never", true)
    local app = App.new(env)
    app:onReaderReady()

    read_to(env, app, 100, 6 * 60)
    app:onCloseDocument()

    assert.are.same({}, env.requests)
  end)

  it("never asks the network manager to turn wifi on", function()
    local env, app = open_linked({ online = false })
    env.turn_on_wifi = function() error("the plugin must not turn wifi on") end

    read_to(env, app, 40, 6 * 60)
    app:onCloseDocument()
  end)
end)
