# The Gateway mints DeviceTokens and keeps them

Reader identity has only ever come from Google (ADR-0001), verified once at the Gateway (ADR-0007),
and the Gateway has held no state of its own: it checked a token, set a header, and forwarded. An
e-reader cannot sign in with Google. It has no browser to run the consent flow in, and the ID
token that flow produces lasts an hour, which is no use to a device that syncs when it happens to
have wifi. The Hardcover KOReader plugin works because Hardcover hands out a static token to paste
into a config file; ReadingTracker had nothing like it.

## Decision

The Gateway accepts a second kind of bearer token, the **DeviceToken**: a random 256-bit secret it
minted itself for a signed-in Reader, presented as `Authorization: Bearer rt_…`. The `rt_` prefix
routes it to a lookup instead of the JWT path, so a JWT is never looked up and a DeviceToken is
never parsed. A token that is found authenticates the request as its Reader with the same subject
claim a Google token would carry, and from there on nothing differs: the same reader header, the
same stripped Authorization, the same pace (ADR-0013). Catalog and Library never learn that
DeviceTokens exist.

To do that the Gateway keeps a table — the first state it has ever owned — in a schema of its own
in the shared Postgres (ADR-0004), migrated under its own advisory lock on boot (ADR-0006). The
table holds a hash of each secret, never the secret; the secret is returned exactly once, at
minting, and a Reader who loses it mints another.

Minting, listing and revoking are served by the Gateway itself rather than proxied, because it
owns the table, and they are the only endpoints that refuse a DeviceToken caller: a principal
that arrived by DeviceToken carries a claim saying so, and the in-person policy turns it away with
a 403. A token lifted from a lost device can act as the Reader until revoked, but it cannot mint
itself successors.

Tokens do not expire. Revocation is by hand, and a token's last use is recorded so a stale one is
visible on the Devices page.

## Considered Options

**Google's OAuth device-code flow** ("go to google.com/device and type this code") keeps Google as
the only identity provider, but it puts a token-refresh loop in Lua on an e-reader and a
limited-input-device consent screen in the Google Cloud console, for one reader. Rejected as far
more machinery than the problem.

**Self-contained signed tokens** (a JWT the Gateway mints with a persisted key) need no table, but
revoking one means rotating the key for every device at once. Per-token revocation was the
requirement, so a lookup it is.

**A separate Identity service** owning the table would keep the Gateway stateless in name, at the
cost of a service that exists to serve one table to one caller. The Gateway is already the single
place identity is decided; the table sits with the decision.

**Expiring tokens** were rejected because on an e-reader an expired token means syncing silently
stops one day. A revoke button beside a "last used" date is the better tool.

## Consequences

- The Gateway needs a connection string and refuses to start without one, like Catalog and
  Library. Its health check now says whether it can reach its schema, not just whether it is up.
- Gateway tests start a throwaway Postgres, which they never needed before.
- A DeviceToken is as powerful as the Reader's own session everywhere except `/api/devices`. That
  is the trade: scoping tokens to "reporting progress only" would be safer, and would mean a
  device could not add a book to the shelf, which is half of what the plugin is for.
- A plain SHA-256 protects the table against leaking; the secret's 256 bits of randomness are what
  make guessing one hopeless. A password hash would cost every device request and buy nothing,
  because a random secret is not a password.
