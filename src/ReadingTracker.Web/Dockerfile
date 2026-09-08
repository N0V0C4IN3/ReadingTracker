# Web deliberately does not follow the publish-then-runtime-image shape the other three
# services use. It is a Blazor WebAssembly app: the browser executes it, this container's only
# job is to hand out the same static files and dev-server behaviour `dotnet run` already gives
# on a host machine, so this is that command, containerised as-is rather than reinvented as a
# static file server with its own MIME/compression configuration to get right. Production
# hosting is Azure Static Web Apps (ADR-0005), not this image — this exists for a one-command
# local `docker compose up`.
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src

COPY src/ReadingTracker.Web/ReadingTracker.Web.csproj src/ReadingTracker.Web/
RUN dotnet restore src/ReadingTracker.Web/ReadingTracker.Web.csproj

COPY src/ReadingTracker.Web/ src/ReadingTracker.Web/

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "run", "--project", "src/ReadingTracker.Web", "--no-launch-profile", "--no-restore"]
