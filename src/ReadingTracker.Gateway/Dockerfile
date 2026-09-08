# Built from the repo root (docker-compose.yml sets that as the build context), because the
# project needs to be reachable without assuming where in a larger tree it sits.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restored before the rest of the source is copied in, so editing a .cs file doesn't invalidate
# the restore layer. The Gateway has no ProjectReferences — Catalog and Library are addresses
# in configuration, not assemblies it links against.
COPY src/ReadingTracker.Gateway/ReadingTracker.Gateway.csproj src/ReadingTracker.Gateway/
RUN dotnet restore src/ReadingTracker.Gateway/ReadingTracker.Gateway.csproj

COPY src/ReadingTracker.Gateway/ src/ReadingTracker.Gateway/
RUN dotnet publish src/ReadingTracker.Gateway/ReadingTracker.Gateway.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ReadingTracker.Gateway.dll"]
