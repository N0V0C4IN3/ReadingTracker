#!/bin/sh
#
# Watches the road a visitor takes to the Gateway -- the public hostname, resolved by public DNS
# to Tailscale's Funnel ingress -- and restarts the tailscale sidecar when that road goes dark.
#
# The hostname must NOT be resolved the normal way: from anywhere on the tailnet, MagicDNS answers
# with the node's own 100.x address and the probe would reach the Gateway directly, healthy as
# ever, while every visitor was getting a failed handshake. DNS-over-HTTPS to a public resolver
# is how curl is made to take the outside road.
#
# Three misses a minute apart before acting, so one slow answer or a resolver hiccup does not
# bounce a working ingress; after a restart the ingress is given two minutes to come back before
# it is judged again. Runs in the funnel-watch service of docker-compose.pi.yml.
set -u

url=${FUNNEL_URL:?FUNNEL_URL must be set}
resolver=${FUNNEL_RESOLVER:-https://cloudflare-dns.com/dns-query}
misses=0

apk add --no-cache curl >/dev/null

say() { echo "$(date -u +%FT%TZ) $*"; }

sidecar() {
    project=$(docker inspect -f '{{index .Config.Labels "com.docker.compose.project"}}' "$HOSTNAME")
    docker ps -q -f "label=com.docker.compose.project=$project" -f label=com.docker.compose.service=tailscale
}

say "watching $url"

while :; do
    if curl -sf -m 15 -o /dev/null --doh-url "$resolver" "$url"; then
        if [ "$misses" -gt 0 ]; then say "back: reachable again"; fi
        misses=0
    else
        misses=$((misses + 1))
        say "miss $misses/3: $url not reachable through the Funnel ingress"
        if [ "$misses" -ge 3 ]; then
            id=$(sidecar)
            if [ -n "$id" ]; then
                say "restarting the tailscale sidecar ($id)"
                docker restart "$id" >/dev/null
            else
                say "no tailscale sidecar found to restart"
            fi
            misses=0
            sleep 120
            continue
        fi
    fi
    sleep 60
done
