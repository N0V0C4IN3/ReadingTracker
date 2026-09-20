-- The plugin's one way of talking to ReadingTracker: every request goes through the Gateway,
-- carries the DeviceToken, and speaks JSON. What comes back is either a decoded body or a
-- problem the plugin can act on — "token refused", "wait a minute", "offline" — rather than a
-- status code the rest of the plugin would have to interpret for itself.

local json = require("readingtracker.vendor.json")

local Client = {}
Client.__index = Client

-- How a request failed, when it did. `kind` is what the plugin branches on.
Client.UNAUTHORIZED = "unauthorized"   -- the DeviceToken was refused: revoked, or never right
Client.FORBIDDEN = "forbidden"         -- known, but not allowed to do this
Client.RATE_LIMITED = "rate_limited"   -- the Gateway said to wait a minute
Client.NOT_FOUND = "not_found"
Client.CONFLICT = "conflict"
Client.REFUSED = "refused"             -- validation: `reasons` says what was wrong
Client.UNAVAILABLE = "unavailable"     -- the service could not answer just now
Client.OFFLINE = "offline"             -- nothing reached the Gateway at all
Client.FAILED = "failed"               -- anything else

-- env must provide send(request) -> status, body_text  |  nil, "offline"
function Client.new(config, env)
  local base = tostring(config.base_url or "")
  if base:sub(-1) ~= "/" then
    base = base .. "/"
  end

  return setmetatable({
    base_url = base,
    token = config.token,
    env = env,
  }, Client)
end

function Client:search_by_isbn(isbn)
  local body, err = self:request("GET", "api/books/search?isbn=" .. isbn)
  if err then
    return nil, err
  end
  return body.results or {}, nil
end

function Client:search(title, author)
  local query = "api/books/search?title=" .. Client.escape(title or "")
  if author and author ~= "" then
    query = query .. "&author=" .. Client.escape(author)
  end
  local body, err = self:request("GET", query)
  if err then
    return nil, err
  end
  return body.results or {}, nil
end

function Client:add_book(book)
  return self:request("POST", "api/books", book)
end

function Client:library()
  return self:request("GET", "api/library")
end

function Client:add_to_library(book_id)
  return self:request("POST", "api/library", { bookId = book_id })
end

function Client:report_bookmark(entry_id, percent, occurred_at)
  return self:request("PUT", "api/library/" .. entry_id .. "/bookmark", {
    percent = percent,
    occurredAt = occurred_at,
  })
end

function Client:set_status(entry_id, status)
  return self:request("PUT", "api/library/" .. entry_id .. "/status", { status = status })
end

-- Sends one request. Returns the decoded body (or an empty table for no body) and nil, or nil
-- and a problem { kind = ..., status = ..., reasons = { ... } }.
function Client:request(method, path, body)
  local request = {
    method = method,
    url = self.base_url .. path,
    headers = {
      ["Authorization"] = "Bearer " .. tostring(self.token),
      ["Accept"] = "application/json",
    },
  }

  if body ~= nil then
    request.headers["Content-Type"] = "application/json"
    request.body = json.encode(body)
  end

  local status, text = self.env:send(request)

  if status == nil then
    return nil, { kind = Client.OFFLINE }
  end

  if status >= 200 and status < 300 then
    if text == nil or text == "" then
      return {}, nil
    end
    local ok, decoded = pcall(json.decode, text)
    if not ok then
      return nil, { kind = Client.FAILED, status = status }
    end
    return decoded, nil
  end

  local problem = { kind = Client.kind_of(status), status = status, reasons = {} }

  if status == 400 and text and text ~= "" then
    local ok, decoded = pcall(json.decode, text)
    if ok and type(decoded) == "table" and type(decoded.errors) == "table" then
      for _, messages in pairs(decoded.errors) do
        for _, message in ipairs(messages) do
          table.insert(problem.reasons, message)
        end
      end
    end
  end

  return nil, problem
end

function Client.kind_of(status)
  if status == 401 then return Client.UNAUTHORIZED end
  if status == 403 then return Client.FORBIDDEN end
  if status == 404 then return Client.NOT_FOUND end
  if status == 409 then return Client.CONFLICT end
  if status == 429 then return Client.RATE_LIMITED end
  if status == 400 then return Client.REFUSED end
  if status == 502 or status == 503 or status == 504 then return Client.UNAVAILABLE end
  return Client.FAILED
end

function Client.escape(text)
  return (tostring(text):gsub("[^%w%-%._~]", function(c)
    return string.format("%%%02X", c:byte())
  end))
end

return Client
