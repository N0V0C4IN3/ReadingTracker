# Google remains the identity provider; Identity service only maps profiles

Google issues and owns the identity token; the Gateway validates it on every request against Google's public keys, and the Identity service does nothing more than map `GoogleId → internal UserId` plus store preferences. We considered having Identity perform its own OAuth code exchange and mint its own session/JWT, but that duplicates what Google already does correctly and is the part of an auth system most likely to be insecure if built under portfolio time pressure.
