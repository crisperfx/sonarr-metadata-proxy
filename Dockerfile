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
# Non-root user for the runtime (the entrypoint drops privileges via runuser).
# setcap lets that user bind port 443 for the intercepted skyhook.sonarr.tv.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libcap2-bin \
    && rm -rf /var/lib/apt/lists/* \
    && (id -u app >/dev/null 2>&1 || useradd --uid 1654 --create-home --shell /usr/sbin/nologin app) \
    && mkdir -p /home/app \
    && chown app:app /home/app \
    && setcap 'cap_net_bind_service=+ep' /usr/share/dotnet/dotnet

COPY --chown=app:app --from=build /app/publish .

# AniList -> AniDB -> TVDB mapping datasets (baked at build time so the proxy can
# translate AniList search hits into real TheTVDB ids without any extra service).
RUN mkdir -p /app/datamaps \
    && curl -fsSL -o /app/datamaps/anime.json https://raw.githubusercontent.com/anime-and-manga/lists/main/anime.json \
    && curl -fsSL -o /app/datamaps/anime-list-full.xml https://raw.githubusercontent.com/Anime-Lists/anime-lists/master/anime-list-full.xml

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
    ANILIST_DATAMAP_DIR=/app/datamaps \
    CACHE_TTL_MINUTES=1440 \
    SEARCH_RESULT_LIMIT=10 \
    CORS_ALLOWED_ORIGINS= \
    DATA_DIR=/app/data

EXPOSE 443 9697

ENTRYPOINT ["/app/entrypoint.sh"]