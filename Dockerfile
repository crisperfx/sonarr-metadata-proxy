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

# Sonarr-side injection files (CA hook, override-UI hook and JS). Copied into
# DATA_DIR on start and kept in sync on every start, so a pulling user needs no
# local files at all and always gets the current version.
COPY entrypoint.sh /app/entrypoint.sh
COPY init/ /app/init/
RUN chmod +x /app/entrypoint.sh /app/init/*.sh

# Pre-filled defaults so container UIs (Dockhand/Portainer) show working values right away.
# Users only need to override TMDB_API_KEY (or TMDB_API_TOKEN) and CORS_ALLOWED_ORIGINS.
ENV METADATA_SOURCE=tmdb \
    TMDB_API_KEY= \
    TMDB_API_TOKEN= \
    TMDb_LANGUAGE=en-US \
    ENABLE_TVDB_FALLBACK=true \
    PORT=9697 \
    LOG_LEVEL=Information \
    SKIP_TLS=false \
    SKYHOOK_BASE_URL=https://skyhook.sonarr.tv \
    SKYHOOK_RESOLVER_URL=https://cloudflare-dns.com/dns-query \
    CACHE_TTL_MINUTES=1440 \
    SEARCH_RESULT_LIMIT=10 \
    CORS_ALLOWED_ORIGINS= \
    DATA_DIR=/app/data

EXPOSE 443 9697

ENTRYPOINT ["/app/entrypoint.sh"]