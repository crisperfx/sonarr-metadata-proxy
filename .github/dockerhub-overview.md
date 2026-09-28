# Sonarr Metadata Proxy

A sidecar that gives your **unmodified Sonarr** access to metadata from **TMDB, AniList, MAL, TVMaze, AniDB, and TVDB** — no fork, no patched Sonarr, just a Docker container.

## Features

- **Multiple metadata sources** — pick TMDB, TVDB, AniList, MAL, TVMaze, or AniDB per series via the **Metadata source** dropdown, or per search via **Search via**.
- **Anime-ready** — AniList/MAL/AniDB resolve to a single continuous season; no mixed artwork (AniList uses its own posters/banners, MAL pulls backgrounds from Tenrai).
- **Reset series to TVDB** — one button wipes all provider IDs, episode mappings and overrides, pins the series to TVDB and triggers a Sonarr **Refresh & Scan** to re-fetch cleanly.
- **Resolver auditing** — see exactly why a series resolved to a show/episode list (diagnoses wrong-show lookups).
- **AniDB poster proxy** — posters are proxied through the container's own endpoint to bypass cdn hotlink blocking (browsers that send a Referer get blocked by AniDB otherwise).
- **Drop-in** — mounts a CA cert and patches Sonarr's UI via a shared folder; Sonarr resolves `skyhook.sonarr.tv` to the proxy. Works with stock Sonarr and Docker UIs (Synology, Portainer, Dockhand, ...).

## Quick start

```bash
git clone https://github.com/crisperfx/sonarr-metadata-proxy.git
cd sonarr-metadata-proxy
cp .env.example .env
# set TMDB_API_KEY (only needed if you use TMDB)
docker compose up -d
```

- Proxy: management port **9697**, TLS on **443**.
- Sonarr stock image included; shared volumes for CA certs, mappings and UI injection scripts.
- Open Sonarr at `http://<your-ip>:8989` → **Add Series** → search → results from your chosen source.
- Per series: **Metadata source** dropdown → TMDB / TVDB / AniList / MAL / TVMaze / AniDB → **Refresh & Scan**.

## Configuration (all optional)

See `.env.example` / the README on GitHub for the full variable table (`TMDB_API_KEY`, `METADATA_SOURCE`, `ANIDB_CLIENT`, `CORS_ALLOWED_ORIGINS`, `LOG_LEVEL`, `CACHE_TTL_MINUTES`, and more).

## Changelog

<!-- CHANGELOG -->