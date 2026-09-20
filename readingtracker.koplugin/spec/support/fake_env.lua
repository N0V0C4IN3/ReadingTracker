-- A stand-in for everything the plugin takes from KOReader and the network, so a spec can open a
-- document, turn pages, close it and see what the plugin did — which requests it sent, which
-- prompts it raised, what it remembered about the document — without a Kindle in the loop.
--
-- The shape is the `env` table main.lua builds from the real KOReader; app.lua only ever talks
-- to this shape.

local json = require("readingtracker.vendor.json")

local FakeEnv = {}
FakeEnv.__index = FakeEnv

function FakeEnv.new(options)
  options = options or {}
  local env = setmetatable({}, FakeEnv)

  env.now = options.now or 1000000
  env.online = options.online ~= false
  env.config = { base_url = "http://gateway.test/", token = options.token or "rt_secret" }

  -- What the plugin remembers about the open document; the real thing is KOReader's doc settings.
  env.stored = {}
  env.flushed = 0

  env.document = {
    file = options.file or "/mnt/us/documents/book.epub",
    title = options.title or "The Left Hand of Darkness",
    authors = options.authors or "Ursula K. Le Guin",
    identifiers = options.identifiers,
    page = options.page or 1,
    page_count = options.page_count or 300,
  }

  -- Everything shown to the reader, in order. A prompt records its choices and is answered by
  -- the spec through `answer`; a notice just records its text.
  env.prompts = {}
  env.notices = {}
  env.answers = {}

  -- Every request the plugin made, and the scripted answers for them.
  env.requests = {}
  env.responses = {}

  return env
end

-- The env interface app.lua uses ---------------------------------------------------------------

function FakeEnv:clock()
  return self.now
end

function FakeEnv:is_online()
  return self.online
end

function FakeEnv:read_setting(key)
  return self.stored[key]
end

function FakeEnv:save_setting(key, value)
  self.stored[key] = value
end

function FakeEnv:flush_settings()
  self.flushed = self.flushed + 1
end

function FakeEnv:notify(text)
  table.insert(self.notices, text)
end

-- prompt = { text = ..., choices = { { id = "yes", label = "Yes" }, ... } }; the answer is the
-- id of the choice the reader tapped, or nil when they dismissed it.
function FakeEnv:prompt(prompt, on_answer)
  prompt.kind = "prompt"
  table.insert(self.prompts, prompt)
  on_answer(table.remove(self.answers, 1))
end

-- form = { title = ..., fields = { { id = "title", label = "Title", value = "..." }, ... } };
-- answered with a table of values by field id, or nil when dismissed.
function FakeEnv:ask_text(form, on_submit)
  form.kind = "form"
  table.insert(self.prompts, form)
  on_submit(table.remove(self.answers, 1))
end

-- list = { title = ..., items = { { id = ..., text = ..., detail = ... }, ... } }; answered with
-- the picked item's id, or nil when dismissed.
function FakeEnv:pick(list, on_pick)
  list.kind = "pick"
  table.insert(self.prompts, list)
  on_pick(table.remove(self.answers, 1))
end

-- request = { method = "GET", url = "...", body = table|nil }
-- returns status, body_table_or_nil ; or nil, "offline"
function FakeEnv:send(request)
  table.insert(self.requests, request)

  if not self.online then
    return nil, "offline"
  end

  for _, scripted in ipairs(self.responses) do
    if scripted.method == request.method and request.url:find(scripted.url, 1, true) then
      local body = scripted.body
      if type(body) == "table" then
        body = json.encode(body)
      end
      return scripted.status, body
    end
  end

  return 404, ""
end

-- Spec helpers ---------------------------------------------------------------------------------

function FakeEnv:respond(method, url, status, body)
  table.insert(self.responses, { method = method, url = url, status = status, body = body })
end

function FakeEnv:answer(choice_id)
  table.insert(self.answers, choice_id)
end

function FakeEnv:requests_to(method, url_fragment)
  local found = {}
  for _, request in ipairs(self.requests) do
    if request.method == method and request.url:find(url_fragment, 1, true) then
      table.insert(found, request)
    end
  end
  return found
end

function FakeEnv:last_prompt()
  return self.prompts[#self.prompts]
end

return FakeEnv
