local FakeEnv = require("spec.support.fake_env")
local Installer = require("readingtracker.installer")

local REPO = "N0V0C4IN3/ReadingTracker"
local URL = "https://github.com/N0V0C4IN3/ReadingTracker/releases/download/plugin-v0.3.0/readingtracker.koplugin.zip"
local HOST = "https://release-assets.githubusercontent.com/github-production-release-asset/1234?sig=abc"
local FOLDER = "plugins/readingtracker.koplugin"

-- 0.2.1 installed with the reader's config; the release's link redirecting to the file host.
local function device()
  local env = FakeEnv.new()
  env:install_plugin("0.2.1")
  env.downloads[URL] = { status = 302, location = HOST }
  env.downloads[HOST] = { zip = FakeEnv.plugin_zip("0.3.0") }
  return env, Installer.new(env, REPO)
end

-- Every path left on the device outside the plugin's folder, the reader's books aside.
local function beside(env)
  local found = {}
  for path in pairs(env.files) do
    if path:sub(1, #FOLDER + 1) ~= FOLDER .. "/" then
      table.insert(found, path)
    end
  end
  table.sort(found)
  return found
end

local function as_it_was(env)
  assert.are.equal("main 0.2.1", env.files[FOLDER .. "/main.lua"])
  assert.are.equal("version 0.2.1", env.files[FOLDER .. "/_meta.lua"])
  assert.are.equal("the reader's config", env.files[FOLDER .. "/readingtracker_config.lua"])
  assert.are.same({}, beside(env))
end

describe("installing an update", function()
  it("follows the redirect, swaps the plugin in, and keeps the reader's config", function()
    local env, installer = device()

    assert.is_true(installer:install(URL))

    assert.are.same({ URL, HOST }, env.fetched)
    assert.are.equal("main 0.3.0", env.files[FOLDER .. "/main.lua"])
    assert.are.equal("app 0.3.0", env.files[FOLDER .. "/readingtracker/app.lua"])
    assert.are.equal("the reader's config", env.files[FOLDER .. "/readingtracker_config.lua"])
    assert.are.same({}, beside(env))
  end)

  it("installs a device that has no config yet", function()
    local env, installer = device()
    env.files[FOLDER .. "/readingtracker_config.lua"] = nil

    assert.is_true(installer:install(URL))

    assert.is_nil(env.files[FOLDER .. "/readingtracker_config.lua"])
  end)

  it("starts clean over what an earlier update left behind", function()
    local env, installer = device()
    env.files["plugins/readingtracker.koplugin.update/readingtracker.koplugin/main.lua"] = "half an earlier update"
    env.files["plugins/readingtracker.koplugin.old/main.lua"] = "an earlier plugin"

    assert.is_true(installer:install(URL))

    assert.are.same({}, beside(env))
  end)

  it("resolves a redirect from the host's root", function()
    local env, installer = device()
    env.downloads[URL] = { status = 302, location = "/N0V0C4IN3/ReadingTracker/elsewhere.zip" }
    env.downloads["https://github.com/N0V0C4IN3/ReadingTracker/elsewhere.zip"] = { zip = FakeEnv.plugin_zip("0.3.0") }

    assert.is_true(installer:install(URL))
  end)

  it("refuses a file that is not this repository's release", function()
    local env, installer = device()

    local ok, reason = installer:install("https://evil.example/readingtracker.koplugin.zip")

    assert.is_nil(ok)
    assert.are.equal("the release's file is not on GitHub", reason)
    assert.are.same({}, env.fetched)
    as_it_was(env)
  end)

  it("refuses to be redirected off https", function()
    local env, installer = device()
    env.downloads[URL] = { status = 302, location = "http://release-assets.githubusercontent.com/x" }

    local ok, reason = installer:install(URL)

    assert.is_nil(ok)
    assert.are.equal("the download was sent somewhere that is not https", reason)
    as_it_was(env)
  end)

  it("gives up on a download that keeps being redirected", function()
    local env, installer = device()
    env.downloads[URL] = { status = 302, location = URL }

    local ok, reason = installer:install(URL)

    assert.is_nil(ok)
    assert.are.equal("the download kept being redirected", reason)
    assert.are.equal(Installer.HOPS, #env.fetched)
    as_it_was(env)
  end)

  it("leaves the plugin as it was when the download is not there", function()
    local env, installer = device()
    env.downloads[HOST] = nil

    local ok, reason = installer:install(URL)

    assert.is_nil(ok)
    assert.are.equal("the download answered 404", reason)
    as_it_was(env)
  end)

  it("leaves the plugin as it was when the zip cannot be unpacked", function()
    local env, installer = device()
    env.failing["extract"] = "could not unpack readingtracker.koplugin/main.lua"

    local ok, reason = installer:install(URL)

    assert.is_nil(ok)
    assert.are.equal("could not unpack readingtracker.koplugin/main.lua", reason)
    as_it_was(env)
  end)

  it("refuses a zip holding a link before unpacking anything", function()
    local env, installer = device()
    local zip = FakeEnv.plugin_zip("0.3.0")
    zip["readingtracker.koplugin/books"] = FakeEnv.LINK
    env.downloads[HOST] = { zip = zip }
    local extracted = false
    function env:extract() extracted = true return true end

    local ok, reason = installer:install(URL)

    assert.is_nil(ok)
    assert.are.equal("the zip holds something that is not the plugin's (readingtracker.koplugin/books)", reason)
    assert.is_false(extracted)
    as_it_was(env)
  end)

  it("refuses a zip with a path that climbs out of the folder", function()
    local env, installer = device()
    local zip = FakeEnv.plugin_zip("0.3.0")
    zip["readingtracker.koplugin/../../escaped.txt"] = "out"
    env.downloads[HOST] = { zip = zip }

    local ok, reason = installer:install(URL)

    assert.is_nil(ok)
    assert.are.equal("the zip holds something that is not the plugin's (readingtracker.koplugin/../../escaped.txt)", reason)
    as_it_was(env)
  end)

  it("refuses a zip that is not the plugin", function()
    local env, installer = device()
    env.downloads[HOST] = { zip = { ["something-else/readme.txt"] = "not a plugin" } }

    local ok, reason = installer:install(URL)

    assert.is_nil(ok)
    assert.are.equal("the download is not the plugin", reason)
    as_it_was(env)
  end)

  it("leaves the plugin as it was when the config cannot be carried over", function()
    local env, installer = device()
    env.failing["copy:" .. FOLDER .. "/readingtracker_config.lua"] = "No space left on device"

    local ok, reason = installer:install(URL)

    assert.is_nil(ok)
    assert.are.equal("your config could not be carried over (No space left on device)", reason)
    as_it_was(env)
  end)

  it("leaves the plugin as it was when it cannot be moved aside", function()
    local env, installer = device()
    env.failing["rename:" .. FOLDER] = "busy"

    local ok, reason = installer:install(URL)

    assert.is_nil(ok)
    assert.are.equal("the plugin could not be moved aside (busy)", reason)
    as_it_was(env)
  end)

  it("puts the old plugin back when the new one cannot be put in place", function()
    local env, installer = device()
    env.failing["rename:plugins/readingtracker.koplugin.update/readingtracker.koplugin"] = "busy"

    local ok, reason, stranded = installer:install(URL)

    assert.is_nil(ok)
    assert.are.equal("the new plugin could not be put in place (busy)", reason)
    assert.is_nil(stranded)
    as_it_was(env)
  end)

  it("says where the old plugin is when it cannot be put back either", function()
    local env, installer = device()
    env.failing["rename:plugins/readingtracker.koplugin.update/readingtracker.koplugin"] = "busy"
    env.failing["rename:plugins/readingtracker.koplugin.old"] = "busy"

    local ok, _, stranded = installer:install(URL)

    assert.is_nil(ok)
    assert.are.equal("plugins/readingtracker.koplugin.old", stranded)
    assert.are.equal("main 0.2.1", env.files["plugins/readingtracker.koplugin.old/main.lua"])
  end)
end)

describe("reading a redirect", function()
  it("keeps an absolute location, and puts a rooted one on the host that sent it", function()
    assert.are.equal("https://b.test/x", Installer.resolve("https://a.test/y/z", "https://b.test/x"))
    assert.are.equal("https://a.test/x?q=1", Installer.resolve("https://a.test/y/z", "/x?q=1"))
  end)

  it("puts one from the scheme on the scheme that sent it", function()
    assert.are.equal("https://b.test/x", Installer.resolve("https://a.test/y", "//b.test/x"))
  end)

  it("is nothing for a location relative to a path, and the download says it could not follow", function()
    assert.is_nil(Installer.resolve("https://a.test/y/z", "x"))

    local env, installer = device()
    env.downloads[URL] = { status = 302, location = "elsewhere.zip" }
    local _, reason = installer:install(URL)
    assert.are.equal("the download was redirected somewhere it could not follow", reason)
  end)
end)

describe("what a plugin's zip may hold", function()
  it("is files and folders inside it", function()
    assert.is_nil(Installer.refused({
      { path = "readingtracker.koplugin", mode = "directory" },
      { path = "readingtracker.koplugin/main.lua", mode = "file" },
      { path = "readingtracker.koplugin/a..b.lua", mode = "file" },
    }))
  end)

  it("is not a link, a device, an absolute path, a climb out or an empty name", function()
    assert.are.equal("x", Installer.refused({ { path = "x", mode = "link" } }))
    assert.are.equal("x", Installer.refused({ { path = "x", mode = "other" } }))
    assert.are.equal("/etc/x", Installer.refused({ { path = "/etc/x", mode = "file" } }))
    assert.are.equal("a/../../x", Installer.refused({ { path = "a/../../x", mode = "file" } }))
    assert.are.equal("..", Installer.refused({ { path = "..", mode = "directory" } }))
    assert.are.equal("", Installer.refused({ { path = "", mode = "file" } }))
  end)
end)

describe("which file is a release's", function()
  it("is this repository's release download, whatever its case", function()
    assert.is_true(Installer.from_release(URL, REPO))
    assert.is_true(Installer.from_release(URL, "n0v0c4in3/readingtracker"))
  end)

  it("is not another repository's, another host's, or one over http", function()
    assert.is_false(Installer.from_release("https://github.com/someone/else/releases/download/x.zip", REPO))
    assert.is_false(Installer.from_release("https://github.com.evil.test/N0V0C4IN3/ReadingTracker/releases/download/x.zip", REPO))
    assert.is_false(Installer.from_release((URL:gsub("^https", "http")), REPO))
    assert.is_false(Installer.from_release(nil, REPO))
  end)
end)
