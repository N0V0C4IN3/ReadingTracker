# Built from the repo root (docker-compose.yml sets that as the build context), because the
# project needs to be reachable without assuming where in a larger tree it sits.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restored before the rest of the source is copied in, so editing a .cs file doesn't invalidate
# the restore layer. Catalog has no ProjectReferences, so its own csproj is all restore needs.
COPY src/ReadingTracker.Catalog/ReadingTracker.Catalog.csproj src/ReadingTracker.Catalog/
RUN dotnet restore src/ReadingTracker.Catalog/ReadingTracker.Catalog.csproj

COPY src/ReadingTracker.Catalog/ src/ReadingTracker.Catalog/
RUN dotnet publish src/ReadingTracker.Catalog/ReadingTracker.Catalog.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# The .NET 8+ runtime images already default ASPNETCORE_URLS to this; set explicitly so the
# port compose maps is never guesswork.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ReadingTracker.Catalog.dll"]
