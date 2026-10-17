-- Quotes: a highlight the reader saves from KOReader's highlight menu, kept in ReadingTracker on
-- the book's page. Sent on purpose, one at a time; deleting or restyling the highlight afterwards
-- sends nothing (ADR-0017).
--
-- One way out: a Quote saved joins the ones kept from before, and they go in the order they
-- were saved, each with its own save time. Whatever cannot go now waits for the next chance —
-- the plugin never turns wifi on. Driven by app.lua, through its env and client.

local bit = require("bit")
local Client = require("readingtracker.client")

local Quotes = {}
Quotes.__index = Quotes

-- What is remembered per document: { quote, ... }, saved while they could not be sent.
Quotes.KEPT = "quotes_kept"

-- A Quote the Gateway answers in a way that is neither a yes nor a reason waits for this many
-- tries, then is dropped: a captive portal clears, an answer that never changes does not.
Quotes.TRIES = 5

local NOT_KEPT = "The Quote was not kept: this document is not linked to a book on your shelf."
local OFFLINE = "Offline — the Quote is kept and sent at the next chance."

function Quotes.new(app)
  return setmetatable({ app = app, env = app.env }, Quotes)
end

-- The highlight menu's "Save to ReadingTracker". highlight = { datetime, start, passage, note,
-- page, chapter }, as KOReader keeps the annotation: datetime when it was made, start where it
-- begins (an xpointer, or a PDF's { page, x, y }), page the page it begins on now.
function Quotes:save(highlight)
  local app, env = self.app, self.env
  if app.disabled then
    return app:complain({ kind = Client.UNAUTHORIZED })
  end

  local passage = Quotes.blank_to_nil(highlight.passage)
  if not passage then
    return env:notify("There is no text in this highlight to keep.")
  end
  if not tonumber(highlight.page) then
    return env:notify("KOReader did not say where this highlight is, so it was not kept.")
  end

  local quote = {
    id = Quotes.identity(highlight.datetime, highlight.start),
    passage = passage,
    note = Quotes.blank_to_nil(highlight.note),
    percent = app:percent_of(tonumber(highlight.page)),
    chapter = Quotes.blank_to_nil(highlight.chapter),
    saved_at = app.iso(env:clock()),
  }

  if app:linked_entry() then
    return self:keep_and_send(quote)
  end

  -- Linking needs the library, so it cannot wait for later the way a Quote can.
  if not env:is_online() then
    return env:notify("Connect to wifi to link this document to a book, then save the Quote again.")
  end

  -- An explicit save links even a document the reader said never to ask about: the tap asks.
  app:start_linking(function(entry_id)
    if entry_id then
      self:keep_and_send(quote)
    else
      env:notify(NOT_KEPT)
    end
  end)
end

function Quotes:keep_and_send(quote)
  self:keep(quote)
  local outcome = self:send_kept(quote.id)
  if outcome == "created" then
    self.env:notify("Saved to ReadingTracker")
  elseif outcome == "updated" then
    self.env:notify("Updated in ReadingTracker")
  end
end

-- Sends what is kept, oldest first, and says what came of it. Returns what came of the Quote
-- `mine`, if one is named and it went: "created" or "updated", as Library answered — 201 for a
-- Quote it did not have, 200 for one whose note it updated.
function Quotes:send_kept(mine)
  local app, env = self.app, self.env
  local kept = env:read_setting(Quotes.KEPT) or {}
  if #kept == 0 or app.disabled or not app:linked_entry() then
    return nil
  end
  if not env:is_online() then
    if mine then
      env:notify(OFFLINE)
    end
    return nil
  end

  local left, others, outcome, stopped, failed, dropped = {}, 0, nil, nil, nil, 0
  for index, quote in ipairs(kept) do
    local _, err, status = app.client:save_quote(app:linked_entry(), quote)
    if not err then
      if quote.id == mine then
        outcome = status == 201 and "created" or "updated"
      else
        others = others + 1
      end
    elseif Quotes.waits(err) then
      -- What stopped this one stops the rest: they all go to the same place.
      for rest = index, #kept do
        table.insert(left, kept[rest])
      end
      stopped = err
      break
    elseif err.kind == Client.FAILED then
      -- This one's answer, not the Gateway's: the rest still go, and it tries again a few times.
      quote.tries = (quote.tries or 0) + 1
      if quote.tries < Quotes.TRIES then
        table.insert(left, quote)
        failed = err
      else
        dropped = dropped + 1
      end
    else
      -- Refused as it is, so refused again later: dropped, and said why.
      app:complain(err)
    end
  end

  env:save_setting(Quotes.KEPT, #left > 0 and left or nil)
  env:flush_settings()

  if stopped or failed then
    self:say_kept(stopped or failed)
  end
  if dropped > 0 then
    env:notify(string.format("%s could not be sent after %d tries and %s dropped.",
      dropped == 1 and "A Quote" or (dropped .. " Quotes"), Quotes.TRIES, dropped == 1 and "was" or "were"))
  end
  if others > 0 then
    env:notify(others == 1 and "Sent a kept Quote to ReadingTracker"
      or string.format("Sent %d kept Quotes to ReadingTracker", others))
  end
  return outcome
end

-- Once, for all that is left: why it waits, and that it does.
function Quotes:say_kept(err)
  local app, env = self.app, self.env
  if err.kind == Client.OFFLINE then
    return env:notify(OFFLINE)
  end
  if err.kind == Client.NOT_FOUND then
    return env:notify("The book this document is linked to is no longer on your shelf."
      .. " Quotes are kept: link the document again from the menu and they are sent.")
  end
  local problem = app:problem(err)
  env:notify(problem .. " Quotes are kept and sent at the next chance.")
end

-- A later save of the same highlight replaces the one kept: only its last note matters.
function Quotes:keep(quote)
  local kept = {}
  for _, waiting in ipairs(self.env:read_setting(Quotes.KEPT) or {}) do
    if waiting.id ~= quote.id then
      table.insert(kept, waiting)
    end
  end
  table.insert(kept, quote)
  self.env:save_setting(Quotes.KEPT, kept)
  self.env:flush_settings()
end

-- What stops every Quote, not just one: no answer, a refused token the reader can put right, or
-- a book gone from the shelf until the document is linked again. Each waits for later.
function Quotes.waits(err)
  return err.kind == Client.OFFLINE or err.kind == Client.UNAVAILABLE or err.kind == Client.RATE_LIMITED
    or err.kind == Client.UNAUTHORIZED or err.kind == Client.NOT_FOUND
end

-- A highlight's identity, stable for as long as the highlight exists: when KOReader made it, to
-- the second. It is the one thing about a highlight KOReader never changes: extending it or
-- moving its edges changes where it begins, and its text. Two highlights made in the same
-- second would share it, which a reader's hands do not manage. A highlight with no time, from
-- an older KOReader, is named by where it begins instead. Letters, digits and '-', as Library asks.
function Quotes.identity(datetime, start)
  local stamp = tostring(datetime or ""):gsub("%D", "")
  if stamp ~= "" then
    return "ko-" .. stamp
  end
  if type(start) == "table" then
    start = string.format("%s:%.2f:%.2f", tostring(start.page), tonumber(start.x) or 0, tonumber(start.y) or 0)
  end
  return "ko-at-" .. Quotes.fnv1a(tostring(start or ""))
end

-- FNV-1a, 32 bits, as hex: short, deterministic, and enough to tell beginnings apart.
function Quotes.fnv1a(text)
  local hash = 2166136261
  for index = 1, #text do
    hash = bit.bxor(hash, text:byte(index)) % 4294967296
    -- hash * 16777619, kept to 32 bits without losing precision in a double.
    hash = (bit.lshift(hash, 24) % 4294967296 + hash * 403) % 4294967296
  end
  return string.format("%08x", hash)
end

function Quotes.blank_to_nil(text)
  if type(text) ~= "string" or not text:find("%S") then
    return nil
  end
  return text
end

return Quotes
