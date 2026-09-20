# The Gateway bounds each reader's pace

Anyone with a Google account can sign in to the deployed demo, and nothing behind the Gateway can
tell a reader from a loop. One signed-in caller could search until the Google Books daily quota
was spent — taking Catalog search away from every other reader for the rest of the day — or add
Books by hand until the shared Catalog was full of them. Neither needs a vulnerability; both are
the ordinary API used quickly.

## Decision

The Gateway rate-limits every request, keyed by the reader it has just verified (ADR-0007), using
ASP.NET Core's built-in limiter with fixed one-minute windows and no queue:

- **Searches**: 10 a minute per reader. Each reaches out to two providers and spends shared quota.
- **Books added by hand**: 5 a minute per reader. Each is a row in the shared Catalog, for everyone.
- **DeviceTokens minted**: 5 a minute per reader (added with ADR-0014). Each is a permanent
  credential.
- **Everything else**: 300 a minute per reader, a ceiling nobody reaches by hand.
- **Callers with no verified reader**: 60 a minute per client address. They only ever get a 401,
  so this bounds what a stream of junk tokens can cost, and it draws on no reader's allowance.

A refusal is a 429 with `Retry-After` in seconds. The frontend turns it into a notice that says
to wait a minute rather than the generic "could not be reached". The numbers live in the
`RateLimits` configuration section so a test can lower them and a deployment could raise them; the
defaults are what the demo runs with.

The limiter runs after authentication and before authorization: a reader is counted as
themselves whatever address they come from, and a caller with no reader is counted by address
before being turned away.

## Considered Options

**Limiting in Catalog** was rejected: Catalog does not know who is asking beyond a header it is
told to trust, and the point is to bound a *reader*, which is the Gateway's concept.

**A sliding window or token bucket** would be smoother but harder to explain in a notice. "Ten a
minute" is something a reader can be told; a bucket refilling at a rate is not.

**Limiting anonymous callers per address behind Tailscale Funnel** is imperfect: Funnel relays
through a sidecar, and without forwarded-header handling every anonymous request arrives from the
same address and shares one bucket. Accepted for now, since anonymous requests can only ever be
refused, and the bucket is generous. Forwarded-header handling is the fix if it ever matters.

## Consequences

- The Google Books quota can no longer be spent by one reader. Spending it now takes many
  accounts, which is a different kind of problem from a script.
- A reader who genuinely searches eleven times in a minute is told to wait. This is judged rare
  enough that the trade is right, and the notice says exactly what happened.
- The Gateway's route configuration names two more routes than it did — search and by-hand —
  purely to carry their policies. The catch-all still covers everything else Catalog serves.
