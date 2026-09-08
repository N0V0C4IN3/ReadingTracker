# Built from the repo root (docker-compose.yml sets that as the build context), because the
# project needs to be reachable without assuming where in a larger tree it sits.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restored before the rest of the source is copied in, so editing a .cs file doesn't invalidate
# the restore layer. Library has no ProjectReferences — it reaches Catalog over HTTP, never by
# referencing its assembly — so its own csproj is all restore needs.
COPY src/ReadingTracker.Library/ReadingTracker.Library.csproj src/ReadingTracker.Library/
RUN dotnet restore src/ReadingTracker.Library/ReadingTracker.Library.csproj

COPY src/ReadingTracker.Library/ src/ReadingTracker.Library/
RUN dotnet publish src/ReadingTracker.Library/ReadingTracker.Library.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ReadingTracker.Library.dll"]
