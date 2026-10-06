#!/usr/bin/env bash
# Rebuilds the local stack and waits until it answers. `docker compose up -d` returns once the
# containers have started, which is seconds before they serve: a request straight after it gets
# 000 or ERR_EMPTY_RESPONSE, which looks like a broken change and isn't one.
#
# Run from anywhere: bash tools/dev/up.sh
# Exits 0 once the web app and the three services all answer; non-zero, naming the one that didn't,
# after two minutes.
set -u -o pipefail

cd "$(git rev-parse --show-toplevel)" || exit 1

docker compose --profile services up --build -d 2>&1 | tail -n 3 || exit 1

wait_for() { # name url
    for _ in $(seq 1 60); do
        [ "$(curl -s -o /dev/null -w '%{http_code}' "$2")" = 200 ] && { echo "up: $1 ($2)"; return 0; }
        sleep 2
    done
    echo "up: $1 did not answer at $2 within two minutes; see: docker compose logs $1" >&2
    return 1
}

wait_for catalog http://localhost:5103/health &&
    wait_for library http://localhost:5110/health &&
    wait_for gateway http://localhost:5100/health &&
    wait_for web http://localhost:5200/ &&
    echo "up: ready at http://localhost:5200"
