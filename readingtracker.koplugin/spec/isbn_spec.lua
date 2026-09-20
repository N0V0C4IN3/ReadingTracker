local Isbn = require("readingtracker.isbn")

describe("finding an ISBN in a document's identifiers", function()
  it("reads the plain form", function()
    assert.are.equal("9780441478125", Isbn.from_identifiers("isbn:9780441478125"))
  end)

  it("strips a urn prefix and hyphens", function()
    assert.are.equal("9780441478125", Isbn.from_identifiers("urn:isbn:978-0-441-47812-5"))
  end)

  it("keeps an ISBN-10, check digit and all", function()
    assert.are.equal("044147812X", Isbn.from_identifiers("ISBN 0-441-47812-x"))
    assert.are.equal("044147812X", Isbn.from_identifiers("044147812X"))
  end)

  it("prefers a 13 over a 10 when both are given", function()
    assert.are.equal("9780441478125", Isbn.from_identifiers("isbn:0441478123\nisbn:9780441478125"))
  end)

  it("ignores identifiers that are not ISBNs", function()
    assert.is_nil(Isbn.from_identifiers("calibre:42\nuuid:0d6b2c7e-1b3a-4e2f-9c1a-1234567890ab\nmobi-asin:B000FC1PJI"))
  end)

  it("takes a bare number of the right length", function()
    assert.are.equal("9780441478125", Isbn.from_identifiers("9780441478125"))
  end)

  it("has nothing to say about nothing", function()
    assert.is_nil(Isbn.from_identifiers(nil))
    assert.is_nil(Isbn.from_identifiers(""))
  end)
end)
