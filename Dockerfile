# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Sonarr.MetadataProxy/Sonarr.MetadataProxy.csproj Sonarr.MetadataProxy/
RUN dotnet restore Sonarr.MetadataProxy/Sonarr.MetadataProxy.csproj

COPY src/Sonarr.MetadataProxy/ Sonarr.MetadataProxy/
RUN dotnet publish Sonarr.MetadataProxy/Sonarr.MetadataProxy.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV DATA_DIR=/app/data

EXPOSE 443 9697

ENTRYPOINT ["dotnet", "Sonarr.MetadataProxy.dll"]