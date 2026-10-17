-- HTTPS that knows who answered. KOReader's LuaSec checks no certificate by default, and even
-- when told to it checks only that a chain ends at a trusted root, not that the certificate is
-- for the host asked: any certificate for any domain would pass. The updater installs code, so
-- its requests to GitHub go through here instead — the chain checked against the roots in
-- certs/github.pem, and the certificate's names checked against the host.
--
-- create(cafile) gives LuaSocket's http.request a `create` for one request; the rest is pure.

local TrustedHttps = {}

-- Whether a certificate name covers a host: exactly, or as a wildcard for one label at the
-- front ("*.github.com" covers "api.github.com", not "github.com" or "a.b.github.com").
function TrustedHttps.covers(name, host)
  name, host = tostring(name):lower(), tostring(host):lower()
  if name == host then
    return true
  end
  local suffix = name:match("^%*(%.[^*]+%.[^*]+)$")
  if not suffix or #host <= #suffix or host:sub(-#suffix) ~= suffix then
    return false
  end
  return not host:sub(1, #host - #suffix):find(".", 1, true)
end

-- The DNS names a certificate is for, from its subjectAltName; nothing else counts, as browsers
-- have long held.
function TrustedHttps.names(certificate)
  local ok, extensions = pcall(function() return certificate:extensions() end)
  local alt = ok and type(extensions) == "table" and extensions["2.5.29.17"] or nil
  return type(alt) == "table" and type(alt.dNSName) == "table" and alt.dNSName or {}
end

function TrustedHttps.names_host(names, host)
  for _, name in ipairs(names) do
    if TrustedHttps.covers(name, host) then
      return true
    end
  end
  return false
end

-- A LuaSocket `create` that speaks TLS 1.2 or later, checks the chain against cafile, and
-- refuses a certificate that is not for the host it connected to.
function TrustedHttps.create(cafile, timeout)
  local socket = require("socket")
  local ssl = require("ssl")
  local try = socket.try
  local params = {
    mode = "client",
    protocol = "any",
    options = { "all", "no_sslv2", "no_sslv3", "no_tlsv1", "no_tlsv1_1" },
    verify = { "peer", "fail_if_no_peer_cert" },
    cafile = cafile,
  }

  return function()
    local conn = { sock = try(socket.tcp()) }

    function conn:settimeout(...)
      return self.sock:settimeout(...)
    end

    function conn:connect(host, port)
      try(self.sock:connect(host, port))
      self.sock = try(ssl.wrap(self.sock, params))
      self.sock:sni(host)
      self.sock:settimeout(timeout or 15)
      try(self.sock:dohandshake())
      if not TrustedHttps.names_host(TrustedHttps.names(self.sock:getpeercertificate()), host) then
        self.sock:close()
        try(nil, "the certificate is not for " .. host)
      end
      -- Everything else straight to the TLS socket, as LuaSec's own https does.
      for name, method in pairs(getmetatable(self.sock).__index) do
        if type(method) == "function" and name ~= "settimeout" then
          self[name] = function(wrapper, ...) return method(wrapper.sock, ...) end
        end
      end
      return 1
    end

    return conn
  end
end

return TrustedHttps
