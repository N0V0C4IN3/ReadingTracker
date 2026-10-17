local TrustedHttps = require("readingtracker.trusted_https")

describe("whether a certificate is for a host", function()
  it("takes the exact name, whatever its case", function()
    assert.is_true(TrustedHttps.covers("api.github.com", "api.github.com"))
    assert.is_true(TrustedHttps.covers("API.GitHub.com", "api.github.com"))
  end)

  it("takes a wildcard for one label at the front", function()
    assert.is_true(TrustedHttps.covers("*.github.com", "api.github.com"))
  end)

  it("does not take a wildcard for the bare domain, for two labels, or for a lookalike", function()
    assert.is_false(TrustedHttps.covers("*.github.com", "github.com"))
    assert.is_false(TrustedHttps.covers("*.github.com", "a.b.github.com"))
    assert.is_false(TrustedHttps.covers("*.github.com", "evilgithub.com"))
    assert.is_false(TrustedHttps.covers("*.github.com", "api.github.com.evil.test"))
  end)

  it("does not take a wildcard over a whole top-level domain", function()
    assert.is_false(TrustedHttps.covers("*.com", "github.com"))
    assert.is_false(TrustedHttps.covers("*", "github.com"))
  end)

  it("reads the names from the certificate's subjectAltName", function()
    local certificate = {
      extensions = function() return { ["2.5.29.17"] = { dNSName = { "*.github.com", "github.com" } } } end,
    }

    local names = TrustedHttps.names(certificate)

    assert.is_true(TrustedHttps.names_host(names, "api.github.com"))
    assert.is_false(TrustedHttps.names_host(names, "github.io"))
  end)

  it("finds no names on a certificate without them, so no host is covered", function()
    assert.are.same({}, TrustedHttps.names({ extensions = function() return {} end }))
    assert.are.same({}, TrustedHttps.names({ extensions = function() error("no extensions") end }))
    assert.is_false(TrustedHttps.names_host({}, "api.github.com"))
  end)
end)
