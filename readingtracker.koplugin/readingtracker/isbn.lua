-- Finds an ISBN in a document's identifiers, as ebook metadata spells them: "isbn:978…",
-- "urn:isbn:978-0-…", "ISBN 0441478123", one per line or all on one, with or without hyphens.
-- The Catalog takes either length bare, so what comes out is digits only (and the ISBN-10
-- check digit X, which is not a digit but is part of the number).

local Isbn = {}

-- The first ISBN-13 in the text, or failing that the first ISBN-10. A 13 is preferred because
-- it names an edition to every provider the Catalog asks; a 10 is the same number in an older
-- spelling, and some metadata carries only that.
function Isbn.from_identifiers(identifiers)
  if not identifiers or identifiers == "" then
    return nil
  end

  local ten
  for line in tostring(identifiers):gmatch("[^\r\n]+") do
    local candidate = Isbn.from_line(line)
    if candidate and #candidate == 13 then
      return candidate
    elseif candidate and not ten then
      ten = candidate
    end
  end

  return ten
end

-- One line's worth: strips any "isbn:"/"urn:isbn:"-style prefix, then the separators, and
-- keeps the result only if it is shaped like an ISBN. Anything else — an ASIN, a UUID, a
-- Calibre id — is not one, whatever the prefix claims.
function Isbn.from_line(line)
  local lowered = line:lower()

  -- Only lines that mention isbn, or that are nothing but a number: "calibre:123" is not one.
  if not lowered:find("isbn", 1, true) and lowered:find("[a-z]") then
    return nil
  end

  local bare = lowered:gsub("^.*isbn[^%dx]*", ""):gsub("[%s%-]", "")

  if bare:match("^%d%d%d%d%d%d%d%d%d%d%d%d%d$") then
    return bare
  end

  if bare:match("^%d%d%d%d%d%d%d%d%d[%dx]$") then
    return bare:upper()
  end

  return nil
end

return Isbn
