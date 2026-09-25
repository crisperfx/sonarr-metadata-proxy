# Sonarr Metadata Proxy

A sidecar that gives your **unmodified Sonarr** access to metadata from **TMDB, AniList, MAL, TVMaze, AniDB, and TVDB** — no fork, no patched Sonarr, just a Docker container.

# Screenshots interface

| **Sidenav left**<br>Menu | **Menu**<br>Poster, choice of source | **Overview**<br>Single and add/new search |
|:---:|:---:|:---:|
| <img width="100%" src="https://github.com/user-attachments/assets/a236e426-c8fb-497f-a75a-6cdda9199a1f" /> | <img width="100%" src="https://github.com/user-attachments/assets/1df71bbe-4e2e-41f8-a69b-390eefb116a0" /> | <img width="100%" src="https://github.com/user-attachments/assets/4db02036-5d82-4b79-8475-c22b4ac1d979" /> |

| **Choice of source TVDB**<br>23 seasons, different ep. listing | **Choice of source ANILIST**<br>1 season, different ep. listing |
|:---:|:---:|
| <img width="100%" src="https://github.com/user-attachments/assets/280bc099-1f28-409a-ad0d-1afcbcc80ae6" /> | <img width="100%" src="https://github.com/user-attachments/assets/698fbe7b-b4e8-4a8a-88ea-efb69c7d22c8" /> |

## What you need

1. **Docker** (with Compose v2) or a Docker UI on your NAS/PC (Synology Container Manager, Portainer, Dockhand, etc.)
2. **A free TMDB API key** — <https://www.themoviedb.org/settings/api> → *API* → *Create* → *Developer*. **Only needed if you use TMDB** as a source.
3. **Sonarr** — the compose example below starts one fresh. Already running Sonarr? Edit or change your existing stack.

---

## Quick start (recommended)

```bash
git clone https://github.com/crisperfx/sonarr-metadata-proxy.git
cd sonarr-metadata-proxy
cp .env.example .env
```

Edit `.env` and set your TMDB API key:

```
TMDB_API_KEY=your-tmdb-v3-api-key
```

The compose file already includes **everything needed**:
- The proxy (with TLS on 443, management on 9697)
- Sonarr (stock image)
- Shared volumes for CA certs, mappings, and UI injection scripts
- Docker network with `skyhook.sonarr.tv` alias

```bash
docker compose up -d
```

Open Sonarr at `http://<your-ip>:8989` → **Add Series** → search → results from TMDB.

Go to any series page → **Metadata source** dropdown → pick **TMDB**, **TVDB**, **AniList**, **MAL**, **TVMaze**, or **AniDB** → **Refresh & Scan**.

That's it.

---

## What the dropdowns do

| Dropdown | Choices | What it means | Screenshots |
|---|---|---|---|
| **Metadata source** (per series) | Automatic / TMDB / TVDB / AniList / MAL / TVMaze / AniDB | Where to get this series' details & episodes. *Automatic* uses your global default (`METADATA_SOURCE`). | <img width="320" alt="Metadata source dropdown" src="https://github.com/user-attachments/assets/6dd6c1b3-8dbc-45b1-8263-351f5e308263" /> |
| **Search via** (when adding a series) | Automatic / TMDB / TVDB / AniList / MAL / TVMaze / AniDB | Which provider to search. *Automatic* uses the global default. **Your choice persists** — next time you open Add Series it remembers the last selected provider until you change it. | <img width="320" alt="Search via dropdown" src="https://github.com/user-attachments/assets/0ed0b0a0-96cd-484e-9ab1-51616186b690" /> |

**AniList, MAL & AniDB** are optimized for anime:
- Episodes come back as a single continuous season (since AniList/MAL/AniDB don't have per-season data).
- If the data ever has 2+ seasons, they're kept as-is.
- AniList uses its own artwork (poster + banner). MAL pulls backgrounds from Tenrai. AniDB provides poster + synopsis; one API call returns series + all episodes.
- No MyAnimeList art is mixed into AniList results.

---

## Configuration (all optional)

| Variable | Default | What it does |
|---|---|---|
| `METADATA_SOURCE` | `tmdb` | Default search/detail source: `tmdb`, `tvdb`, `anilist`, `mal`, `tvmaze`, or `anidb`. |
| `TMDB_API_KEY` | — | TMDB v3 API key. **Only needed if you use TMDB** as a source. Get at <https://www.themoviedb.org/settings/api>. |
| `TMDB_API_TOKEN` | — | TMDB v4 bearer token (alternative to the key; wins if both set). |
| `ANIDB_CLIENT` | — | AniDB HTTP API client name. **Only needed if you use AniDB**. Both this and `ANIDB_CLIENT_VERSION` must be set. Register at <https://anidb.net/creq/>. |
| `ANIDB_CLIENT_VERSION` | — | AniDB HTTP API client version. See `ANIDB_CLIENT`. |
| `TMDB_LANGUAGE` | `en-US` | Language for TMDB requests. |
| `ENABLE_TVDB_FALLBACK` | `true` | Fall back to real TVDB when the chosen source fails. |
| `PORT` | `9697` | Management/health port. SkyHook/TLS is always on 443. |
| `SKIP_TLS` | `false` | `true` = disable TLS/443 (dev only, won't work with Sonarr). |
| `CORS_ALLOWED_ORIGINS` | — | Browser origins allowed to call the overrides API (needed for the dropdown). Multiple with commas, `*` = all (e.g. `https://sonarr.example.com,http://192.168.0.143`). |
| `OVERRIDES_API_URL` | — | Set on **Sonarr** container when behind a reverse proxy (e.g. `https://proxy.example.com`). |
| `DATA_DIR` | `/app/data` | Persistent data: `mappings/` (series map + overrides), `certs/` (CA), `init/` (seed files). |
| `ANILIST_DATAMAP_DIR` | `/app/datamaps` | Folder with AniList↔AniDB↔TVDB datasets (baked into the image). |

Set these in `.env`, or as environment variables on the container / in your own compose.

---

## Docker UI (Portainer / Synology / Dockhand)

Prefer clicking? Same result, no repo needed — defaults are baked into the image.

**Step 1 — One-time setup**
- Get a TMDB API key: <https://www.themoviedb.org/settings/api>
- Create a data folder, e.g. `/volume9/docker/config/sonarr-metadata-proxy`

**Step 2 — Create the proxy container, start it blank first**
- Image: `crisperfx/sonarr-metadata-proxy:latest`
- Start **completely blank** and wait until UP. On first start it writes the CA + injection files into your data folder.

**Step 3 — Stop the proxy container, then fill in these settings**

| Setting | Value |
|---|---|
| Name | `sonarr-metadata-proxy (or your choice)` |
| Port mapping | `9697:9697` — only if you need `/info` outside Docker |
| Environment | `TMDB_API_KEY` = `<your key>` — **only if you use TMDB** |
| Environment | `CORS_ALLOWED_ORIGINS` = `http://<sonarr-ip>:8989` (for the dropdown) |
| Volume | `/volume3/docker/config/sonarr-metadata-proxy` → `/app/data` |
| Extra capability | `NET_BIND_SERVICE` — required to bind port 443 |

Restart. The created data folder (step 1)  now contains `01-install-ca.sh`, `50-sonarr-override-ui.sh`, `metadata-proxy-override.js`, `certs/ca.crt`.

**Step 4 — Configure Sonarr (new or existing)**

New: use `lscr.io/linuxserver/sonarr:latest`. Existing: add these mounts + env:

| Field | Value |
|---|---|
| Volume | `/volume3/docker/config/sonarr-metadata-proxy` → `/shared` (read-only) |
| Volume | `.../sonarr-metadata-proxy/01-install-ca.sh` → `/custom-cont-init.d/01-install-ca.sh` (read-only) |
| Volume | `.../sonarr-metadata-proxy/50-sonarr-override-ui.sh` → `/custom-cont-init.d/50-sonarr-override-ui.sh` (read-only) |
| Environment (optional) | `OVERRDS_API_URL` = `https://proxy.example.com` — only behind a reverse proxy |

**Network & DNS**: Sonarr must resolve `skyhook.sonarr.tv` to the proxy (port 443). Put both on the same Docker network and add a hosts entry in Sonarr: `skyhook.sonarr.tv` → proxy container IP.

**Step 5 — Restart both (order matters!)**
1. Proxy → wait until fully UP
2. Sonarr → installs CA + patches UI (log: `index.html patched...`)
3. **Restart Sonarr once more** — the patched UI is now active in the browser.

**Step 6 — Test**
- Sonarr log: `Installing proxied CA for skyhook.sonarr.tv` and `index.html patched with override UI script`
- Open Sonarr → **Add Series** → search → results from TMDB (or pick **AniDB** / **TVMaze** / **MAL** in the **Search via** dropdown)
- Series page → **Metadata source** → **TMDB** / **AniDB** / **TVMaze** / **MAL** / **TVDB** → **Refresh & Scan**

---

**How to share the CA** depends on your proxy's data storage:

| Proxy setup | Mount on Sonarr |
|---|---|
| Proxy uses the compose `certs` volume | `certs:/shared/certs:ro` |
| Proxy uses a plain folder (`DATA_DIR`) | Mount that **whole folder** as `/shared:ro` (its `certs/` → `/shared/certs`, `init/` → `/shared/init`) |

```yaml
volumes:
  - /path/to/proxy/data:/shared:ro
  - /path/to/proxy/data/01-install-ca.sh:/custom-cont-init.d/01-install-ca.sh:ro
  - /path/to/proxy/data/50-sonarr-override-ui.sh:/custom-cont-init.d/50-sonarr-override-ui.sh:ro
```

> ⚠️ Do **not** mount the whole folder onto `/custom-cont-init.d` — mount only the two `.sh` files as shown.

---

## Behind an HTTPS reverse proxy (e.g. Synology DSM)

If Sonarr is `https://sonarr.example.com`, the picker defaults to `http://<proxy>:9697` (blocked as mixed content).

1. Add a reverse proxy rule: `https://proxy.example.com` → `http://<proxy-ip>:9697` (different subdomain).
2. Set on **Sonarr-container**: `OVERRIDES_API_URL=https://proxy.example.com`
3. Set on **proxy-container**: `CORS_ALLOWED_ORIGINS=https://sonarr.example.com`
4. Test: series page → dropdown → **TMDB** → **Refresh & Scan**

---

## Updating

```bash
docker pull crisperfx/sonarr-metadata-proxy:latest
docker restart sonarr-metadata-proxy
docker restart sonarr
```

Pull only for **new code** (features/fixes). For config changes (env vars) just restart Sonarr — the hook re-applies on every start.

Your data (`mappings/`, `certs/`) is never touched.

---

## Security notes

- Runs unprivileged (drops to `app` user after fixing volume ownership).
- Private keys are `0600` (only the proxy user reads them; only `ca.crt` is shared with Sonarr).
- No secrets in the web UI — the picker reads Sonarr's API key at runtime from the authenticated page.
- TMDB credentials are never logged.
- Outbound calls go to fixed hosts only (TMDB, AniList, Tenrai, SkyHook, Cloudflare DoH, Wikidata).
- CORS is closed by default — `/api/overrides` only answers origins in `CORS_ALLOWED_ORIGINS`.

> ⚠️ The management API on `9697` is **unauthenticated** (the in-browser picker uses it). Don't expose `9697` to the internet; keep it behind your firewall/reverse proxy.

---

## Management API (if you need it)

```bash
curl -X POST http://127.0.0.1:9697/api/overrides \
  -H 'Content-Type: application/json' \
  -d '{"tvdbId": 81189, "source": "tmdb", "tmdbId": 1396}'

curl http://127.0.0.1:9697/api/overrides          # list
curl -X DELETE http://127.0.0.1:9697/api/overrides/81189
```

`tmdbId` is optional; without it the mapping is looked up automatically.

---

## Limitations

- **AniList**: searches TV / TV_SHORT only (excludes movies, specials, OVAs). No TVDB mapping → synthetic ID, details via TMDB (synthetic → TMDB). On empty results or API errors, falls back to TVDB.
- **MAL**: searches TV only (excludes movies, OVAs, music). No TVDB mapping → synthetic ID, falls back to TVDB on error.
- **AniDB**: searches the daily title dump (instant, title-only, no artwork); details/posters fetched when series is opened (cached per day). Requires `ANIDB_CLIENT` + `ANIDB_CLIENT_VERSION`; otherwise `anidb:` falls back to TVDB. No air time → `timeOfDay` is missing.
- Episodes without a TVDB mapping get proxy-local stable IDs.
- TMDB has no air time → `timeOfDay` is missing.

---

## Release / image tags (for maintainers)

```bash
git push origin develop                    # CI: tests + publish :develop (Docker Hub + GHCR)
git tag v1.1.4 && git push origin v1.1.4   # CI: tests + publish :1.1.4 and :latest
```

| Branch/Tag | Published as |
|---|---|
| `develop` | `:develop` (dev build, never `latest`) |
| `main` | `:latest` |
| `vX.Y.Z` | `:X.Y.Z` (e.g. `v1.1.4` → `1.1.4`) |

Test and info: `dotnet test` or `docker compose build sonarr-metadata-proxy`., OVAs). Anime without a known TVDB mapping get a synthetic TVDB ID and appear in search; details/episodes served via TMDB (synthetic → TMDB). On empty results or API errors, falls back to TVDB.
- MAL search filters to `type: tv` (excludes movies, OVAs, music, etc.). Series without a known TVDB mapping get a synthetic TVDB ID; on empty results or API errors, falls back to TVDB.
- Episodes of series without a TVDB mapping get proxy-local (stable) episode ids.
- TMDB has no air time, so `timeOfDay` is missing.
