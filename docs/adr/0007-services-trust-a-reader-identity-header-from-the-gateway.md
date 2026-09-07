# Services trust a reader-identity header set by the Gateway

Library identifies the reader from an `X-Reader-Id` header rather than validating a token itself. ADR-0001 makes the Gateway the single place a Google-issued token is verified; having each service re-verify it would duplicate that logic and give every service a second chance to get it wrong.

## Consequences

**A service that trusts this header must never be publicly routable.** Anyone who can reach it directly can claim to be any reader simply by setting the header. The Gateway is responsible for stripping any inbound `X-Reader-Id` and setting it only from a token it has verified; the deployment is responsible for ensuring the services are reachable only through the Gateway.

This is a deliberate trade, not an oversight: it is the standard shape for a trusted-gateway topology, and it keeps token verification in exactly one place. The risk it carries is a deployment risk (network exposure) rather than an application one, which is where it is easier to reason about and harder to get subtly wrong.

## Considered Options

**Each service validates the Google token itself** was rejected: it duplicates verification across every service, multiplies the places a mistake becomes a security hole, and couples every service to the identity provider that ADR-0001 deliberately confines to one boundary.
