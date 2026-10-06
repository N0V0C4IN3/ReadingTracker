#!/usr/bin/env bash
# Rebuilds the local stack and waits until it answers. `docker compose up -d` returns once the
# containers have started, which is seconds before they serve: a request straight after it gets
# 000 or ERR_EMPTY_RESPONSE, which looks like a broken change and isn't one. (`up --wait` would
# need healthchecks, which the images cannot run: see docker-compose.yml.)
#
# Run from anywhere: bash tools/dev/up.sh
# Exits 0 once the web app and the three services all answer. A failed build prints its whole
# output; a service that has not answered two minutes after the build is named, with how to see
# its logs. The build itself takes a few minutes on top.
set -u

cd "$(git rev-parse --show-toplevel)" || exit 1

out=$(docker compose --profile services up --build -d 2>&1) || {
    printf '%s\n' "$out" >&2
    echo "up: the build or start failed (output above)." >&2
    exit 1
}

# One deadline for all four, rather than two minutes each.
deadline=$((SECONDS + 120))

wait_for() { # service path
    local address url
    address=$(docker compose port "$1" 8080) || { echo "up: $1 publishes no port 8080." >&2; return 1; }
    url="http://localhost:${address##*:}$2"
    until [ "$(curl -s --max-time 2 -o /dev/null -w '%{http_code}' "$url")" = 200 ]; do
        if [ "$SECONDS" -ge "$deadline" ]; then
            echo "up: $1 did not answer at $url within two minutes; see: docker compose logs $1" >&2
            return 1
        fi
        sleep 2
    done
    echo "up: $1 ($url)"
}

wait_for catalog /health &&
    wait_for library /health &&
    wait_for gateway /health &&
    wait_for web / &&
    echo "up: ready at http://localhost:$(docker compose port web 8080 | sed 's/.*://')"
