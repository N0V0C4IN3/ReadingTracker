# The frontend as the Pi serves it (ADR-0016): published, then handed out by nginx, which also
# passes /api/ and /health on to the Gateway, so the app and the API share one origin.
#
# Not src/ReadingTracker.Web/Dockerfile: that one is `dotnet run` for the local stack.
#
#   built by docker-compose.pi.yml, from the repo root, with PUBLIC_ORIGIN set
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src

COPY src/ReadingTracker.Web/ReadingTracker.Web.csproj src/ReadingTracker.Web/
RUN dotnet restore src/ReadingTracker.Web/ReadingTracker.Web.csproj

COPY src/ReadingTracker.Web/ src/ReadingTracker.Web/

# A WebAssembly app has no environment to read at start-up, so where the Gateway is has to be
# baked into a file it ships with. Without it the app carries appsettings.json's localhost and
# loads perfectly, then cannot reach anything. A trailing slash, because HttpClient.BaseAddress
# drops the last path segment without one.
ARG PUBLIC_ORIGIN
RUN case "$PUBLIC_ORIGIN" in \
      https://*|http://localhost:*) ;; \
      *) echo "PUBLIC_ORIGIN must be the https address visitors use (or http://localhost:<port> to try it)" >&2; exit 1 ;; \
    esac \
 && printf '{ "Gateway": { "BaseAddress": "%s/" } }\n' "${PUBLIC_ORIGIN%/}" \
      > src/ReadingTracker.Web/wwwroot/appsettings.Production.json

RUN dotnet publish src/ReadingTracker.Web/ReadingTracker.Web.csproj \
    --configuration Release --no-restore --output /published

# The Content-Security-Policy names every inline script by hash, and Blazor writes the import
# map into index.html at publish time, so the hashes are only known now.
FROM python:3.13-alpine AS policy
COPY deploy/pi/web/csp.py /csp.py
COPY --from=publish /published/wwwroot/index.html /index.html
RUN python /csp.py /index.html > /csp.conf

FROM nginx:1.27-alpine AS final
COPY deploy/pi/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY deploy/pi/web/headers.conf /etc/nginx/headers.conf
COPY --from=policy /csp.conf /etc/nginx/csp.conf
COPY --from=publish /published/wwwroot /usr/share/nginx/html
