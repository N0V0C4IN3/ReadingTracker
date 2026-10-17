local FakeEnv = require("spec.support.fake_env")
local Updater = require("readingtracker.updater")

local RELEASES = "api.github.com/repos/N0V0C4IN3/ReadingTracker/releases"
local DOWNLOADS = "https://github.com/N0V0C4IN3/ReadingTracker/releases/download/"
local DAY = 24 * 60 * 60

local function asset_url(tag)
  return DOWNLOADS .. tag .. "/readingtracker.koplugin.zip"
end

local function release(tag, options)
  options = options or {}
  local assets = {}
  if options.asset ~= false then
    table.insert(assets, { name = "readingtracker.koplugin.zip", browser_download_url = options.url or asset_url(tag) })
  end
  return {
    tag_name = tag,
    draft = options.draft or false,
    prerelease = options.prerelease or false,
    html_url = "https://github.com/N0V0C4IN3/ReadingTracker/releases/tag/" .. tag,
    assets = assets,
  }
end

-- A device with 0.2.1 installed, GitHub answering with the given releases, and each release's
-- zip downloadable.
local function on_github(releases, options)
  options = options or {}
  local env = FakeEnv.new({ version = (options.version == nil) and "0.2.1" or options.version, online = options.online })
  env:respond("GET", RELEASES, options.status or 200, releases)
  env:install_plugin("0.2.1")
  for _, each in ipairs(releases) do
    if each.tag_name then
      env.downloads[asset_url(each.tag_name)] = { zip = FakeEnv.plugin_zip(each.tag_name:match("[%d.]+$")) }
    end
  end
  return env, Updater.new(env)
end

describe("reading a version", function()
  it("takes a plugin tag or a bare version", function()
    assert.are.same({ 0, 3, 0 }, Updater.parse_version("plugin-v0.3.0"))
    assert.are.same({ 1, 12 }, Updater.parse_version("1.12"))
    assert.are.same({ 2 }, Updater.parse_version("2"))
  end)

  it("is nothing for what is not a version", function()
    assert.is_nil(Updater.parse_version("latest"))
    assert.is_nil(Updater.parse_version("0.3."))
    assert.is_nil(Updater.parse_version("0..3"))
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
  local repo = "N0V0C4IN3/ReadingTracker"

  it("is the newest plugin release with the zip, whatever order GitHub lists them in", function()
    local latest = Updater.latest({ release("plugin-v0.2.1"), release("plugin-v0.10.0"), release("plugin-v0.3.0") }, repo)

    assert.are.equal("0.10.0", latest.version)
    assert.are.equal(asset_url("plugin-v0.10.0"), latest.url)
  end)

  it("passes over drafts, prereleases, releases without the zip and other tags", function()
    local latest = Updater.latest({
      release("plugin-v0.9.0", { draft = true }),
      release("plugin-v0.8.0", { prerelease = true }),
      release("plugin-v0.7.0", { asset = false }),
      release("v5.0.0"),
      release("plugin-v0.3.0"),
    }, repo)

    assert.are.equal("0.3.0", latest.version)
  end)

  it("passes over a zip that is not this repository's release file", function()
    local latest = Updater.latest({
      release("plugin-v0.9.0", { url = "https://evil.example/readingtracker.koplugin.zip" }),
      release("plugin-v0.8.0", { url = "http://github.com/N0V0C4IN3/ReadingTracker/releases/download/plugin-v0.8.0/readingtracker.koplugin.zip" }),
      release("plugin-v0.7.0", { url = "https://github.com/someone/else/releases/download/plugin-v0.7.0/readingtracker.koplugin.zip" }),
      release("plugin-v0.3.0"),
    }, repo)

    assert.are.equal("0.3.0", latest.version)
  end)

  it("is nothing when there is no plugin release at all", function()
    assert.is_nil(Updater.latest({ release("v5.0.0") }, repo))
    assert.is_nil(Updater.latest({}, repo))
  end)
end)

describe("checking from the menu", function()
  it("offers a newer version, installs it on update, and asks for a restart", function()
    local env, updater = on_github({ release("plugin-v0.3.0"), release("plugin-v0.2.1") })
    env:answer("update")

    updater:check()

    local prompt = env:last_prompt()
    assert.are.equal("ReadingTracker 0.3.0 is available — you have 0.2.1. Update now?", prompt.text)
    assert.are.same({ "update", "later", "skip" }, { prompt.choices[1].id, prompt.choices[2].id, prompt.choices[3].id })
    assert.are.equal("main 0.3.0", env.files["plugins/readingtracker.koplugin/main.lua"])
    assert.are.same({ "ReadingTracker 0.3.0 is installed. Restart KOReader to use it." }, env.restarts)
  end)

  it("shows that it is downloading until the install is done", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    env:answer("update")

    updater:check()

    assert.are.equal(1, #env.working_on)
    assert.are.equal("Downloading ReadingTracker 0.3.0…", env.working_on[1].text)
    assert.is_false(env.working_on[1].open)
  end)

  it("asks GitHub as itself, its certificate checked, with no ReadingTracker token", function()
    local env, updater = on_github({ release("plugin-v0.2.1") })

    updater:check()

    local request = env:requests_to("GET", RELEASES)[1]
    assert.is_true(request.trusted)
    assert.are.equal("ReadingTracker-KOReader/0.2.1", request.headers["User-Agent"])
    assert.is_nil(request.headers["Authorization"])
  end)

  it("installs nothing on later", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    env:answer("later")

    updater:check()

    assert.are.equal("main 0.2.1", env.files["plugins/readingtracker.koplugin/main.lua"])
    assert.are.same({}, env.restarts)
  end)

  it("says so when the installed version is the latest", function()
    local env, updater = on_github({ release("plugin-v0.2.1"), release("plugin-v0.2.0") })

    updater:check()

    assert.are.equal(0, #env.prompts)
    assert.are.same({ "ReadingTracker 0.2.1 is the latest version" }, env.notices)
  end)

  it("says it is the latest when GitHub has no plugin release at all", function()
    local env, updater = on_github({})

    updater:check()

    assert.are.same({ "ReadingTracker 0.2.1 is the latest version" }, env.notices)
  end)

  it("says to connect when the device is offline, and asks nobody", function()
    local env, updater = on_github({ release("plugin-v0.3.0") }, { online = false })

    updater:check()

    assert.are.equal(0, #env.requests)
    assert.are.same({ "Connect to wifi to check for updates" }, env.notices)
  end)

  it("says GitHub did not answer, and leaves the plugin alone", function()
    local env, updater = on_github({}, { status = 403 })

    updater:check()

    assert.are.same({ "GitHub did not answer the update check (403)" }, env.notices)
    assert.are.equal(0, #env.prompts)
  end)

  it("says why GitHub could not be reached when it was not just being offline", function()
    local env, updater = on_github({})
    function env:send(request)
      table.insert(self.requests, request)
      return nil, "the certificate is not for api.github.com"
    end

    updater:check()

    assert.are.same({ "GitHub could not be reached just now (the certificate is not for api.github.com)" }, env.notices)
  end)

  it("cannot update a copy that does not say its version, and says how to get one", function()
    local env, updater = on_github({ release("plugin-v0.3.0") }, { version = false })

    updater:check()

    assert.are.equal(0, #env.requests)
    assert.are.equal(0, #env.prompts)
    assert.are.same({ "This copy of ReadingTracker does not say its version, so it cannot update itself."
      .. " Install the latest release by hand from https://github.com/N0V0C4IN3/ReadingTracker/releases" }, env.notices)
  end)

  it("says where to get it by hand when the update cannot be installed", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    env.failing["unpack"] = "the zip is damaged"
    env:answer("update")

    updater:check()

    assert.are.same({}, env.restarts)
    assert.are.equal(
      "The update could not be installed (the zip is damaged). The plugin is as it was; it can be installed by hand from https://github.com/N0V0C4IN3/ReadingTracker/releases/tag/plugin-v0.3.0",
      env.notices[#env.notices])
  end)

  it("says where the plugin is when it could not be put back", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    env.failing["rename:plugins/readingtracker.koplugin.update/readingtracker.koplugin"] = "busy"
    env.failing["rename:plugins/readingtracker.koplugin.old"] = "busy"
    env:answer("update")

    updater:check()

    assert.are.equal(
      "The update could not be installed (the new plugin could not be put in place (busy)), and the plugin could not be put back."
        .. " It is at plugins/readingtracker.koplugin.old: rename it to readingtracker.koplugin, or install 0.3.0 by hand from https://github.com/N0V0C4IN3/ReadingTracker/releases/tag/plugin-v0.3.0",
      env.notices[#env.notices])
  end)

  it("offers a skipped version again, since the reader asked", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    env.preferences[Updater.SKIPPED] = "0.3.0"
    env:answer("later")

    updater:check()

    assert.are.equal(1, #env.prompts)
  end)

  it("after installing, asks for the restart again rather than offering the update again", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    env:answer("update")
    updater:check()
    local asked = #env.requests

    updater:check()

    assert.are.equal(asked, #env.requests)
    assert.are.equal(1, #env.prompts)
    assert.are.equal(2, #env.restarts)
  end)

  it("looks at another repository's releases when the config says so", function()
    local env = FakeEnv.new({ version = "0.2.1" })
    env.config.update_repo = "someone/fork"
    env:respond("GET", "api.github.com/repos/someone/fork/releases", 200, {})

    Updater.new(env):check()

    assert.are.equal(1, #env:requests_to("GET", "api.github.com/repos/someone/fork/releases"))
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

  it("waits an hour after GitHub did not answer, and says nothing about it", function()
    local env, updater = on_github({}, { status = 503 })

    updater:onNetworkConnected()
    env:advance(Updater.RETRY_AFTER - 1)
    updater:onNetworkConnected()
    env:advance(1)
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

  it("says nothing of a copy that does not say its version", function()
    local env, updater = on_github({ release("plugin-v0.3.0") }, { version = false })

    updater:onNetworkConnected()

    assert.are.equal(0, #env.requests)
    assert.are.same({}, env.notices)
  end)

  it("stays quiet when the reader turned it off", function()
    local env, updater = on_github({ release("plugin-v0.3.0") })
    updater:set_automatic(false)

    updater:onNetworkConnected()

    assert.are.equal(0, #env.requests)
  end)
end)
