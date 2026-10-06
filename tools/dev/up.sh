#!/usr/bin/env bash
# Rebuilds the local stack and waits until it answers. `docker compose up -d` returns once the
# containers have started, which is seconds before they serve: a request straight after it gets
# 000 or ERR_EMPTY_RESPONSE, which looks like a broken change and isn't one.
#
# Run from anywhere: bash tools/dev/up.sh
# Exits 0 once the web app and the three services all answer. A failed build prints its whole
# output; a service that has not answered two minutes after the build is named, with how to see
# its logs. The build itself takes a few minutes on top.
set -u -o pipefail

cd "$(git rev-parse --show-toplevel)" || exit 1

log=$(mktemp)
trap 'rm -f "$log"' EXIT
if ! docker compose --profile services up --build -d > "$log" 2>&1; then
    cat "$log" >&2
    echo "up: the build or start failed (output above)." >&2
    exit 1
fi

# One deadline for all four, rather than two minutes each.
deadline=$((SECONDS + 120))

wait_for() { # service url
    until [ "$(curl -s --max-time 2 -o /dev/null -w '%{http_code}' "$2")" = 200 ]; do
        if [ "$SECONDS" -ge "$deadline" ]; then
            echo "up: $1 did not answer at $2 within two minutes; see: docker compose logs $1" >&2
            return 1
        fi
        sleep 2
    done
    echo "up: $1 ($2)"
}

# The ports are docker-compose.yml's.
wait_for catalog http://localhost:5103/health &&
    wait_for library http://localhost:5110/health &&
    wait_for gateway http://localhost:5100/health &&
    wait_for web http://localhost:5200/ &&
    echo "up: ready at http://localhost:5200"
