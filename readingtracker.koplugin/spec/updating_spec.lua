local FakeEnv = require("spec.support.fake_env")
local Updater = require("readingtracker.updater")

local RELEASES = "api.github.com/repos/N0V0C4IN3/ReadingTracker/releases"
local DAY = 24 * 60 * 60

local function release(tag, options)
  options = options or {}
  local assets = {}
  if options.asset ~= false then
    table.insert(assets, {
      name = "readingtracker.koplugin.zip",
      browser_download_url = "https://github.com/N0V0C4IN3/ReadingTracker/releases/download/" .. tag .. "/readingtracker.koplugin.zip",
    })
  end
  return {
    tag_name = tag,
    draft = options.draft or false,
    prerelease = options.prerelease or false,
    html_url = "https://github.com/N0V0C4IN3/ReadingTracker/releases/tag/" .. tag,
    assets = assets,
  }
end

local function on_github(releases, options)
  options = options or {}
  local env = FakeEnv.new({ version = options.version or "0.2.1", online = options.online, install_fails = options.install_fails })
  env:respond("GET", RELEASES, options.status or 200, releases)
  return env, Updater.new(env)
end

describe("reading a version", function()
  it("takes a plugin tag, a v tag or a bare version", function()
    assert.are.same({ 0, 3, 0 }, Updater.parse_version("plugin-v0.3.0"))
    assert.are.same({ 1, 12 }, Updater.parse_version("v1.12"))
    assert.are.same({ 2 }, Updater.parse_version("2"))
  end)

  it("is nothing for what is not a version", function()
    assert.is_nil(Updater.parse_version("latest"))
    assert.is_nil(Updater.parse_version("0.3."))
    assert.is_nil(Updater.parse_version(nil))
  end)

  it("orders by number, part by part, not as text", function()
    assert.is_true(Updater.newer({ 0, 10, 0 }, { 0, 9, 9 }))
    assert.is_true(Updater.newer({ 1 }, { 0, 99 }))
    assert.is_false(Updater.newer({ 0, 3 }, { 0, 3, 0 }))
    assert.is_false(Updater.newer({ 0, 2, 1 }, { 0, 3, 0 }))
  end)
end)

describe("choosing the release", function()
  it("is the newest plugin release with the zip, whatever order GitHub lists them in", function()
    local latest = Updater.latest({ release("plugin-v0.2.1"), release("plugin-v0.10.0"), release("plugin-v0.3.0") })

    assert.are.equal("0.10.0", latest.version)
    assert.are.equal("https://github.com/N0V0C4IN3/ReadingTracker/releases/download/plugin-v0.10.0/readingtracker.koplugin.zip", latest.url)
  end)

  it("passes over drafts, prereleases, releases without the zip and other tags", function()
    local latest = Updater.latest({
      release("plugin-v0.9.0", { draft = true }),
      release("plugin-v0.8.0", { prerelease = true }),
      release("plugin-v0.7.0", { asset = false }),
      release("v5.0.0"),
      release("plugin-v0.3.0"),
    })

    assert.are.equal("0.3.0", latest.version)
  end)

  it("is nothing when there is no plugin release at all", function()
    assert.is_nil(Updater.latest({ release("v5.0.0") }))
    assert.is_nil(Updater.latest({}))
  end)
end)

describe("checking from the menu", function()
  it("offers a newer version, and installs it and asks for a restart on update", function()
    local env, updater = on_github({ release("plugin-v0.3.0"), release("plugin-v0.2.1") })
    env:answer("update")

    updater:check(true)

    local prompt = env:last_prompt()
    assert.are.equal("ReadingTracker 0.3.0 is available — you have 0.2.1. Update now?", prompt.text)
    assert.are.same({ "update", "later", "skip" }, { prompt.choices[1].id, prompt.choices[2].id, prompt.choices[3].id })
    assert.are.same({ "https://github.com/N0V0C4IN3/ReadingTracker/releases/download/plugin-v0.3.0/readingtracker.koplugin.zip" }, env.installed)
    assert.are.same({ "ReadingTracker 0.3.0 is installed. Restart KOReader to use it." }, env.restarts)
  end)

  it("asks GitHub as itself, with no ReadingTracker token", function()
    local env, updater = on_github({ release("plugin-v0.2.1") })

    updater:check(true)

    local request = env:requests_to("GET", RELEASES)[1]
    assert.are.equal("ReadingTracker-KOReader/0.2.1", request.headers["User-Agent"])
    assert.is_nil(request.headers["Authorization"])
  end)

  it("installs nothing on later", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    env:answer("later")

    updater:check(true)

    assert.are.same({}, env.installed)
    assert.are.same({}, env.restarts)
  end)

  it("says so when the installed version is the latest", function()
    local env, updater = on_github({ release("plugin-v0.2.1"), release("plugin-v0.2.0") })

    updater:check(true)

    assert.are.equal(0, #env.prompts)
    assert.are.same({ "ReadingTracker 0.2.1 is the latest version" }, env.notices)
  end)

  it("says to connect when the device is offline, and asks nobody", function()
    local env, updater = on_github({ release("plugin-v0.3.0") }, { online = false })

    updater:check(true)

    assert.are.equal(0, #env.requests)
    assert.are.same({ "Connect to wifi to check for updates" }, env.notices)
  end)

  it("says GitHub did not answer, and leaves the plugin alone", function()
    local env, updater = on_github({}, { status = 403 })

    updater:check(true)

    assert.are.same({ "GitHub did not answer the update check (403)" }, env.notices)
    assert.are.equal(0, #env.prompts)
  end)

  it("says where to get it by hand when the update cannot be installed", function()
    local env, updater = on_github({ release("plugin-v0.3.0") }, { install_fails = "no way to unpack a zip here" })
    env:answer("update")

    updater:check(true)

    assert.are.same({}, env.restarts)
    assert.are.equal(
      "The update could not be installed (no way to unpack a zip here). The plugin is as it was; it can be installed by hand from https://github.com/N0V0C4IN3/ReadingTracker/releases/tag/plugin-v0.3.0",
      env.notices[#env.notices])
  end)

  it("offers a skipped version again, since the reader asked", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    env.preferences[Updater.SKIPPED] = "0.3.0"
    env:answer("later")

    updater:check(true)

    assert.are.equal(1, #env.prompts)
  end)
end)

describe("checking by itself", function()
  it("checks when the device comes online, and says nothing when there is nothing new", function()
    local env, updater = on_github({ release("plugin-v0.2.1") })

    updater:onNetworkConnected()

    assert.are.equal(1, #env:requests_to("GET", RELEASES))
    assert.are.same({}, env.notices)
    assert.are.equal(0, #env.prompts)
  end)

  it("offers a newer version when it finds one", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    env:answer("later")

    updater:onNetworkConnected()

    assert.are.equal(1, #env.prompts)
  end)

  it("asks once a day at most", function()
    local env, updater = on_github({ release("plugin-v0.2.1") })

    updater:onNetworkConnected()
    env:advance(DAY - 1)
    updater:onNetworkConnected()
    env:advance(1)
    updater:onNetworkConnected()

    assert.are.equal(2, #env:requests_to("GET", RELEASES))
  end)

  it("tries again at the next connection when GitHub did not answer", function()
    local env, updater = on_github({}, { status = 503 })

    updater:onNetworkConnected()
    updater:onNetworkConnected()

    assert.are.equal(2, #env:requests_to("GET", RELEASES))
    assert.are.same({}, env.notices)
  end)

  it("does not offer a version the reader skipped, and does offer the one after it", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    env:answer("skip")
    updater:onNetworkConnected()
    assert.are.equal("0.3.0", env.preferences[Updater.SKIPPED])

    env:advance(DAY)
    updater:onNetworkConnected()
    assert.are.equal(1, #env.prompts)

    env.responses = {}
    env:respond("GET", RELEASES, 200, { release("plugin-v0.3.1"), release("plugin-v0.3.0") })
    env:answer("later")
    env:advance(DAY)
    updater:onNetworkConnected()
    assert.are.equal(2, #env.prompts)
  end)

  it("stays quiet when the reader turned it off", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    updater:set_automatic(false)

    updater:onNetworkConnected()

    assert.are.equal(0, #env.requests)
  end)
end)
