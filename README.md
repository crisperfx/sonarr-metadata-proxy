# Screenshots of modded interface

<img width="1183" height="659" alt="image" src="https://github.com/user-attachments/assets/2fff7998-f02d-458d-9fc2-98f800225782" />
<img width="1000" height="734" alt="image" src="https://github.com/user-attachments/assets/6b83eac3-1273-4f26-ba65-77d2564d684b" />
<img width="1108" height="926" alt="image" src="https://github.com/user-attachments/assets/af45bbb1-1515-406c-b3d9-e716bc934aca" />
<img width="1160" height="878" alt="image" src="https://github.com/user-attachments/assets/b2b1854f-e0b9-480a-86a2-dfd20c5803d5" />

# Choice of source TVDB (30 seasons, different ep. listing)

<img width="1173" height="1016" alt="image" src="https://github.com/user-attachments/assets/70bc5db2-a41f-40ec-8779-d8906996d4c5" />

# Choice of source TMDB (24 seasons, different ep. listing)

<img width="1225" height="1009" alt="image" src="https://github.com/user-attachments/assets/43656a7b-a6fc-499d-896a-e3d2ee43af81" />

# Sonarr Metadata Proxy

A sidecar that feeds an **unmodified Sonarr** with metadata from **TMDB, AniList, MAL, and TVDB** by transparently
intercepting Sonarr's metadata requests (`skyhook.sonarr.tv` / TVDB) and translating them
back into the exact JSON contract Sonarr expects. No fork, no patched Sonarr, no local
.NET SDK required — runs as a prebuilt Docker image.

## What you need

1. **Docker** — either Docker with Compose v2, or a Docker UI on your NAS / PC
   (Synology Container Manager, Dockhand, Portainer, ...).
2. **A free TMDB API key** — <https://www.themoviedb.org/settings/api> (sign up, then
   *API* → *Create* → *Developer*). It is the only secret you must fill in.
3. **Sonarr** — not required to have yet: the compose example below starts a fresh one.
   Got an existing Sonarr? See [Option C](#option-c--you-already-run-sonarr).

## How it works (30 seconds)

```
 your browser                    Docker network
      │  http://<ip>:8989                │
      ▼                                  ▼
 ┌───────────┐  metadata request   ┌────────────────┐   TMDB/AniList/MAL  ┌────────┐
 │  Sonarr   │ ─ skyhook.sonarr.tv ─▶  metadata      │ ───────────────────▶ │ TMDB   │
 │ (stock)   │   (port 443, alias)  │  proxy (443/  │ ◀──────────────────── │ AniList│
 └───────────┘                      │   9697)       │                      │ MAL    │
      ▲                             └────────────────┘                      └────────┘
      ▲                             └────────────────┘
      │ picker dropdown (overrides)
      └────────────────── /api/overrides
```

- Sonarr talks to `skyhook.sonarr.tv` exactly as it always does — the DNS alias just makes
  that name resolve to the proxy instead of the real SkyHook. Everything else in Sonarr is
  untouched.
- **Search** uses the configured source (Automatic / TMDB / TVDB / AniList / MAL) with **automatic
  fallback to TVDB** when the chosen source returns no results.
- **Series details/episodes** come from TMDB (with automatic TVDB↔TMDB mapping, fallback to
  real TVDB when a series cannot be mapped).
- **Season handling is data-driven.** Any provider may send seasons: if the metadata carries
  **2+ seasons** (numbered like TVDB, or named like TMDB's season blocks) they are served
  as-is. If the data has **0 or 1 seasons** it is served as **one continuous season** with
  all episodes — exactly right for continuous anime like One Piece (MAL/AniList expose no
  season structure). This applies after the series is resolved, regardless of which provider
  produced the data.
- **Poster-as-fanart fallback**: when a provider has no background artwork, the proxy reuses
  the poster as the fanart image; for MAL it pulls real backgrounds from Tenrai's
  `/anime/{id}/pictures` endpoint first.
- Two tiny "hooks" in Sonarr make it all work automatically: one installs trust for the
  proxy's own CA certificate, the other injects a small **Metadata source** dropdown into
  the Sonarr web UI (per-series TMDB/TVDB/AniList/MAL picker) and a **Search via** provider picker
  (TMDB / TVDB / AniList / MAL) into the Add New search box.

---

## Provider behaviour & fallback summary

| Provider | Search behaviour | No results → fallback | Details / episodes |
|---|---|---|---|
| **Automatic** (default = `METADATA_SOURCE`) | Uses configured default (`tmdb`, `tvdb`, `anilist`, or `mal`). | → TVDB | TMDB primary, TVDB fallback on mapping failure |
| **TMDB only** | `tmdb:` prefix; searches TMDB, maps to TVDB via internal map | → TVDB | TMDB primary, TVDB fallback |
| **TVDB (SkyHook)** | `tvdb:` prefix; direct SkyHook passthrough | *(none — source is TVDB)* | Real TVDB |
| **AniList** | Searches AniList (format TV / TV_SHORT only), maps via bundled Fribb + Anime-Lists datasets to TVDB; series without TVDB mapping get a synthetic ID and are shown | → TVDB (synthetic IDs are decomposed, mapped via TMDB) | TMDB primary (via synthetic ID → TMDB), TVDB fallback |
| **MAL (Tenrai)** | Searches MyAnimeList via the public Tenrai API (type TV only, with internal rate-limit handling), maps via the same bundled datasets; series without TVDB mapping get a synthetic ID and are shown | → TVDB (synthetic IDs are decomposed, mapped via TMDB) | TMDB primary, TVDB fallback; backgrounds from Tenrai `/anime/{id}/pictures` |

**Key points:**
- **Every provider falls back to TVDB on empty results** (except explicit TVDB).
- AniList search filters to `format_in: [TV, TV_SHORT]` (excludes movies/specials/OVAs); MAL filters to `type: tv`.
- Series without a real TVDB mapping get a **synthetic TVDB ID** (1 000 000 000 + AniList/MAL ID) so they appear in search; details are served via TMDB (synthetic → TMDB mapping).
- Search provider preference is persisted in localStorage and on the proxy (`/api/overrides/searchsource`), survives page refresh and container restarts.
- Per-series **Metadata source** dropdown (Automatic / TMDB / TVDB / AniList / MAL) still works as before; overrides stored in `DATA_DIR/mappings/mappings.json`.
- **Fanart fallback**: when a provider offers no background artwork, the proxy serves the poster as backdrop instead (skyhook: `BackdropPath ?? PosterPath`; AniList: banner else poster; MAL: backgrounds from Tenrai pictures, else first poster).

**External automation (Prowlarr, Overseerr, Ombi, Radarr, Sonarr RSS, etc.)**
The search provider choice (via `METADATA_SOURCE` env var or UI dropdown) applies to **all** searches that hit Sonarr's SkyHook endpoint — including those triggered by external automation (Prowlarr, Overseerr, Ombi, Radarr, Sonarr's own RSS/monitoring). There is no separate setting; the configured search provider is global for the proxy.

## Option A — Docker Compose (recommended, ~5 minutes)

The whole stack is one `docker-compose.yml` and one `.env`.

**Step 1 — get the files**

```bash
git clone https://github.com/crisperfx/sonarr-metadata-proxy.git
cd sonarr-metadata-proxy
cp .env.example .env
```

**Step 2 — set only your TMDB API key**

```bash
nano .env          # or open in any editor
```

Change just this line (find it near the top):

```bash
TMDB_API_KEY=your-tmdb-v3-api-key
```

→ replace `your-tmdb-v3-api-key` with your real key from step 2 of *What you need*.
Leave everything else as-is. `CORS_ALLOWED_ORIGINS` can stay empty for a local setup.

**Step 3 — start it**

```bash
docker compose up -d
```

**Step 4 — use it**

- Open Sonarr at `http://<your-ip>:8989` → **Add Series** → search → metadata from TMDB.
- Go to a series page → **Metadata source** dropdown → specify **TMDB**, **TVDB**,
  **AniList**, or **MAL** per series → **Refresh & Scan**.

The complete stack (this is the whole `docker-compose.yml`):

```yaml
# Sonarr Metadata Proxy – docker compose example.
#
# This wires an UNMODIFIED stock Sonarr to the proxy:
#   - the proxy answers skyhook.sonarr.tv (network alias) and serves TMDB metadata,
#   - the CA install hook makes Sonarr trust the proxy's TLS certificate,
#   - the override-UI hook injects the per-series TMDB/TVDB/AniList/MAL picker into Sonarr's web UI
#     plus a "Search via" provider picker (TMDB / TVDB / AniList / MAL) into the add-series search.
#
# Usage:
#   cp .env.example .env      # set TMDB_API_KEY (and CORS_ALLOWED_ORIGINS if needed)
#   docker compose up -d
#
# The image below is the Docker Hub default. Use the GHCR mirror instead by replacing
# it with "ghcr.io/crisperfx/sonarr-metadata-proxy:latest", or uncomment "build: ."
# to build locally.

services:
  sonarr:
    image: lscr.io/linuxserver/sonarr:latest
    container_name: sonarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Etc/UTC
      # Optional, behind an HTTPS reverse proxy (e.g. Synology): point the override
      # dropdown at the proxied management API. Example:
      # - OVERRIDES_API_URL=https://proxy.example.com
    volumes:
      - ./sonarr-data/config:/config
      - ./tv:/tv
      - ./downloads:/downloads
      - certs:/shared/certs:ro
      - ./init/01-install-ca.sh:/custom-cont-init.d/01-install-ca.sh:ro
      - ./init/50-sonarr-override-ui.sh:/custom-cont-init.d/50-sonarr-override-ui.sh:ro
      - ./init:/shared/init:ro
    ports:
      - "8989:8989"
    networks:
      - arrnet
    depends_on:
      sonarr-metadata-proxy:
        condition: service_healthy
    restart: unless-stopped

  sonarr-metadata-proxy:
    image: crisperfx/sonarr-metadata-proxy:latest
    # build: .   # uncomment to build locally instead of pulling a prebuilt image
    container_name: sonarr-metadata-proxy
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    env_file:
      - .env
    volumes:
      - proxy-data:/app/data
      - certs:/app/data/certs
    cap_add:
      - NET_BIND_SERVICE
    ports:
      - "9697:9697"
    networks:
      arrnet:
        aliases:
          - skyhook.sonarr.tv
    expose:
      - "443"
    healthcheck:
      test: ["CMD", "sh", "-c", "test -f /app/data/certs/ca.crt && curl -fsS http://127.0.0.1:9697/health > /dev/null"]
      interval: 5s
      timeout: 3s
      retries: 10
      start_period: 10s
    restart: unless-stopped

volumes:
  proxy-data:
  certs:

networks:
  arrnet:
    driver: bridge
```

**What each piece does** (so you can adapt it with confidence):

| Piece | Why it is there |
|---|---|
| `skyhook.sonarr.tv` network alias on the proxy | Sonarr's metadata requests land on the proxy, not the internet. |
| the two `init/*.sh` mounts + `certs` volume on Sonarr | The hooks trust the proxy's certificate and inject the picker. |
| `cap_add: NET_BIND_SERVICE` on the proxy | Lets the proxy bind port 443 (SkyHook) inside the container. |
| `certs` + `proxy-data` volumes | Persist the CA + your `mappings/` overrides. The **proxy writes its CA into `certs`** (`/app/data/certs`) and Sonarr reads the same volume as `/shared/certs`. That is why both containers mount it. |

---

## Option B — Docker UI (Dockhand / Portainer / Synology Container Manager)

Prefer clicking over files? Same result, no repo needed — all defaults are baked into the
image and the injection files land in your data folder on first start.

**Step 0 — Beforehand (one-time)**

- TMDB API key: <https://www.themoviedb.org/settings/api>
- Create a data folder, e.g. `/volume3/docker/config/sonarr-metadata-proxy`.
  This *is* the volume: Sonarr will bind this same folder.

**Step 1 — Create the proxy container, start it blank first**

- Image: `crisperfx/sonarr-metadata-proxy:latest`.
- Start it **completely blank** and wait until it is UP. On first start the image runs with
  its baked-in defaults and writes the injection files + CA into the data folder.

**Step 2 — Stop it, then fill in these settings**

| Field | Value |
|---|---|
| Name | `sonarr-metadata-proxy` |
| Port mapping (optional) | `9697:9697` — only if you want `/info` reachable outside Docker |
| Environment variable | `TMDB_API_KEY` = `<your key>` |
| Environment variable | `CORS_ALLOWED_ORIGINS` = `http://<sonarr-ip>:8989` — for the dropdown; multiple with "," |
| Volume (host path → container) | `/volume3/docker/config/sonarr-metadata-proxy` → `/app/data` |
| Extra capability | `NET_BIND_SERVICE` — required to bind port 443 |

> Prefer a named volume (e.g. `sonarr-metadata-proxy`)? Use the same one on both containers.

After (re)starting, the folder contains `01-install-ca.sh`, `50-sonarr-override-ui.sh`,
`metadata-proxy-override.js` and `certs/ca.crt`.

**Step 3 — Configure Sonarr (new or existing)**

New: just use `lscr.io/linuxserver/sonarr:latest`. Existing: add the fields below.

| Field | Value |
|---|---|
| Volume | `/volume3/docker/config/sonarr-metadata-proxy` → `/shared` (read-only) |
| Volume | `.../sonarr-metadata-proxy/01-install-ca.sh` → `/custom-cont-init.d/01-install-ca.sh` (read-only) |
| Volume | `.../sonarr-metadata-proxy/50-sonarr-override-ui.sh` → `/custom-cont-init.d/50-sonarr-override-ui.sh` (read-only) |
| Environment variable (optional) | `OVERRIDES_API_URL` = `https://proxy.example.com` — only behind a reverse proxy |

Network & DNS: Sonarr must resolve `skyhook.sonarr.tv` to the proxy (port 443 in Docker).
Put both containers on the same network and give Sonarr a **hosts entry**:
`skyhook.sonarr.tv` → the IP of the proxy container (the IP is stable as long as the proxy
is not recreated). On a shared custom network the IP stays stable too.

> Do **not** mount the whole folder onto `/custom-cont-init.d` — mount only the two
> `.sh` files as shown, otherwise the proxy's `mappings/` data and JS get executed as
> scripts on startup (they just log errors, but it is noisy).

**Step 4 — Restart both, in this order**

1. Start/restart the proxy and wait until it is fully UP;
2. Start Sonarr — on startup it installs the CA and patches its web UI
   (log line `[sonarr-metadata-proxy] index.html patched...`);
3. **Restart Sonarr once more** — the patched UI (`index.html` + picker script)
   is now active in the browser.

**Step 5 — Test**

- Sonarr log should show `[sonarr-metadata-proxy] Installing proxied CA for skyhook.sonarr.tv`
  and `index.html patched with override UI script`.
- Open Sonarr (port `8989`) → **Add Series** → search → results from TMDB.
- Open a series page → **Metadata source** dropdown → **TMDB** → **Refresh & Scan**.

**Step 6 — If you ever recreate the proxy**

- Keep the same folder/volume → mappings and CA stay preserved.
- The IP may change → update Sonarr's hosts entry (or restart both without recreating).

---

## Option C — You already run Sonarr

You do not need this compose's Sonarr. Just add to your existing Sonarr the three things
from Option A / Option B step 3:

1. the CA as `/shared/certs`, 2. the two `init/*.sh` mounts into `/custom-cont-init.d/`,
3. the `skyhook.sonarr.tv` alias or hosts entry.

**How to share the CA depends on how your proxy stores its data:**

- **Proxy runs with the compose `certs` volume** → mount that same volume on Sonarr as
  `certs:/shared/certs:ro` (the proxy writes the CA into it).
- **Proxy uses a plain folder as `DATA_DIR`** (no volumes — e.g. an existing instance
  whose data lives in a folder like `/volume3/docker/config/sonarr-proxy/testmap`) →
  mount that **whole folder** as `/shared` on Sonarr. Its `certs/` then shows up at
  `/shared/certs` and its `init/` at `/shared/init`, no extra mounts needed:
  ```yaml
  volumes:
    - /volume3/docker/config/sonarr-proxy/testmap:/shared:ro
    - /volume3/docker/config/sonarr-proxy/testmap/01-install-ca.sh:/custom-cont-init.d/01-install-ca.sh:ro
    - /volume3/docker/config/sonarr-proxy/testmap/50-sonarr-override-ui.sh:/custom-cont-init.d/50-sonarr-override-ui.sh:ro
  ```

> A named `certs` volume that no container writes to is **empty** — the hook then logs
> "CA not present yet … skipping install". If you see that, make sure the proxy shares
> *its* CA folder (see above), not a separate empty volume.

---

## Updating to a newer image

```bash
docker pull crisperfx/sonarr-metadata-proxy:latest
docker restart sonarr-metadata-proxy
docker restart sonarr
```

**When do you have to pull?** Only to get *new code* (features/fixes) from the project. The
proxy compares its seed files against the image on every start and refreshes them when they
differ, so after a pull → proxy restart the newest hooks/JS are already shared; a Sonarr
restart then embeds them.

**When can you skip the pull?** For purely *configuration* changes:

- changed `OVERRIDES_API_URL` or `CORS_ALLOWED_ORIGINS` → **no new image
  needed**, just `docker restart sonarr`. The Sonarr hook re-applies the current
  environment on every start (and re-patches `index.html` with a cache-busting `?v=`,
  so no hard refresh is ever required).

Your data (`mappings/`, `certs/`) is never touched. Do not edit the seed files by hand —
the image version wins; configure behaviour via environment variables.

---

## Behind an HTTPS reverse proxy (e.g. Synology DSM)

If you open Sonarr as `https://sonarr.example.com` instead of `http://<ip>:8989`, the picker
defaults to `http://<host>:9697`, which is blocked on an HTTPS page (mixed content). Fix:

1. **Add a reverse proxy rule** in DSM → Login Portal → Advanced → Reverse Proxy:
   `https://proxy.example.com` → `http://<metadata-proxy-ip>:9697`.
   Use a *different* subdomain; one hostname cannot route to two backends.
2. **`OVERRIDES_API_URL=https://proxy.example.com`** — set it as environment variable on
   the **Sonarr** container (the init hook embeds it into the UI JS).
3. **`CORS_ALLOWED_ORIGINS=https://sonarr.example.com`** — on the proxy container, keep it
   the **browser origin of Sonarr**.
4. Test: series page → dropdown → **TMDB** → **Refresh & Scan**.

Without `OVERRIDES_API_URL` the picker falls back to `http://<host>:9697` — fine for plain
LAN / port-forward use.

---

## Per-series source selection ("Metadata source")

Every series page has a dropdown: **Automatic / TMDB / TVDB / AniList / MAL**.

- **Automatic** = the default source from `METADATA_SOURCE`.
- **TMDB** = always use TMDB servers for this series.
- **TVDB** = always use the real SkyHook/TVDB for this series.
- **AniList** = serve this series using AniList metadata (format TV / TV_SHORT only).
  AniList carries no per-season data for most titles, so the episodes come back as a single
  continuous season (same data-driven flattening as MAL; nothing is ever forced beyond what
  the data says — if the data ever carries 2+ seasons, they are kept).
- **MAL** = serve this series using MAL (Tenrai) metadata — episodes, and backgrounds from
  Tenrai's `/anime/{id}/pictures`. MAL has no season structure, so the series is served as
  one continuous season (data-driven flattening: 0/1 season in the data → one continuous
  season, 2+ → kept as-is). No `mal:`-search binding required, selecting MAL here is enough.

After choosing: **Refresh & Scan** on the series. Overrides are stored in
`DATA_DIR/mappings/mappings.json` (one single file for all series — not a file per
series; per-series files would multiply IO, race, and confuse editing/backup).
Legacy `DATA_DIR/mappings.json` files are migrated into the `mappings/` folder
automatically on startup. The picker never stores your Sonarr API key: it reads Sonarr's
own `window.Sonarr.apiKey` at runtime (only present on authenticated UI pages) and sends it
as the `X-Api-Key` header to `/api/v3/series`, exactly like Sonarr's own frontend does, so
nothing secret is embedded in files that end up next to the (public) login page.

**Note for AniList series without a real TVDB ID:**
When you select **TVDB** in the dropdown for a series that only has a synthetic TVDB ID
(no real TVDB mapping), the UI shows a warning:
> "Let op: deze serie heeft geen echte TVDB-ID. Bij 'TVDB' als bron werkt passthrough niet (fallback naar standaard bron)."
The series will then fall back to the default source (TMDB via synthetic ID decomposition).

### Search provider picker ("Search via")

When adding a series, the search box gets a **Search via** dropdown:

- **Automatic** — uses the default source from `METADATA_SOURCE`
  (`tmdb`, `tvdb`, `anilist`, or `mal`). Searches fall back to TVDB on empty results; TVDB is direct.
- **TMDB only** — prefixes your query with `tmdb:` so the proxy searches TMDB and
  falls back to TVDB on empty results (e.g. to force a TMDB id, type `tmdb:1396`).
- **TVDB (SkyHook)** — prefixes with `tvdb:`, forcing the TVDB listing
  (e.g. `tvdb:breaking bad` or an id `tvdb:81189`). No fallback.
- **AniList** — searches AniList for anime (format TV / TV_SHORT only, excludes movies/specials/OVAs),
  maps the hit to a real TVDB id via the bundled Fribb + Anime-Lists datasets.
  Results without a known TVDB mapping get a synthetic TVDB ID and appear in results;
  API failures and series without mapping fall through to TVDB.
- **MAL** — searches MyAnimeList via the public Tenrai API (type TV only), maps the hit to a real
  TVDB id via the same bundled datasets. Tenrai rate limits are handled internally.
  Results without a known TVDB mapping get a synthetic TVDB ID; API failures and unmatched
  series fall through to TVDB.

**Fallback behaviour for all providers (except explicit TVDB):**
- **Empty results → TVDB fallback** (always).
- **API errors → TVDB fallback**.
- AniList: synthetic TVDB IDs are decomposed to TMDB for detail/episode fetch.

The same prefixes work manually if you type them yourself: `tvdb:id`, `tmdb:id`,
`tvdbid:id`, `imdb:tt...`, `mal:id`, `anilist:id`. The `anilist:` prefix resolves
through AniList; `mal:` resolves through MAL when the bundled mapping data is
available, falling back to AniList and then TVDB.

Both pickers are styled like the Sonarr sidebar (dark `#2a2a2a` panel) and collapse into a small
**Metadata ▸** / **Metasources ▸** pill on the left edge so they never cover the page; tap the pill to
expand, **–** to collapse again.

## Environment variables

| Variable | Default | Description |
|---|---|---|
| `METADATA_SOURCE` | `tmdb` | Primary source: `tmdb`, `tvdb`, `anilist`, or `mal` (passthrough only). |
| `TMDB_API_KEY` | – | TMDB v3 API key (required for TMDB). |
| `TMDB_API_TOKEN` | – | TMDB v4 bearer token, alternative to the key (wins if both set). |
| `TMDb_LANGUAGE` | `en-US` | Language for TMDB requests. |
| `ENABLE_TVDB_FALLBACK` | `true` | Fall back to the real TVDB on mapping/source failure. |
| `PORT` | `9697` | HTTP port for management/health; the SkyHook/TLS listener is always on `443`. |
| `SKIP_TLS` | `false` | `true` = TLS/443 disabled (dev only, not with Sonarr). |
| `SKYHOOK_BASE_URL` | `https://skyhook.sonarr.tv` | Real SkyHook used for the TVDB fallback. |
| `SKYHOOK_RESOLVER_URL` | `https://cloudflare-dns.com/dns-query` | DNS-over-HTTPS for the fallback host. |
| `CORS_ALLOWED_ORIGINS` | empty | Browser origins allowed to call `/api/overrides` (needed for the picker). Multiple with commas, `*` = all. |
| `DATA_DIR` | `/app/data` | Persistent data root: `mappings/` (series map + overrides), `certs/` (CA + certs); `init/` seed files are refreshed from the image. |
| `ANILIST_DATAMAP_DIR` | `/app/datamaps` | Folder with the AniList↔AniDB↔TVDB datasets (`anime.json` + `anime-list-full.xml`); baked into the image, override only to point at your own copies. |

Set these in `.env`, or as environment on the container / in your own compose.

## Security

- **Runs unprivileged** — the container starts as root only to fix the data volume
  ownership, then drops to the unprivileged `app` user (`runuser`). The app process
  has no special capabilities beyond binding port 443 for TLS (granted via `setcap`);
  the compose template also drops all kernel capabilities except the few needed
  (`CHOWN`, `FOWNER`, `SETUID`, `SETGID`, `NET_BIND_SERVICE`, `DAC_OVERRIDE`).
- **Private keys are 0600** — the generated CA and server private keys in
  `DATA_DIR/certs/` (and the `mappings.json` store) are readable only by the proxy
  user. Only `ca.crt` needs to be seen by the Sonarr container.
- **No secrets in the web UI** — the picker reads Sonarr's `window.Sonarr.apiKey` at
  runtime (only served to authenticated UI pages) and sends it as the `X-Api-Key` header,
  matching Sonarr's own frontend; nothing is embedded in static files.
- **TMDB credentials are not logged** — error messages redact `api_key` (the bearer
  token travels only in a header and is never part of URLs).
- **Outgoing requests go to fixed hosts only** (TMDB, AniList, Tenrai for MAL, SkyHook,
  Cloudflare DoH, Wikidata); user input is limited to IDs and escaped search terms, so
  there is no SSRF from the public endpoints.
- **CORS is closed by default** — `/api/overrides` only answers cross-origin browsers
  listed in `CORS_ALLOWED_ORIGINS` (same-origin requests always work).

> The management API on `9697` is **unauthenticated by design** (the in-browser picker
> uses it) and `9697:9697` is published on your LAN. Anyone on the network can read and
> change overrides, and searches against it consume your TMDB quota. Do not expose
> `9697` to the internet; keep it behind your firewall / reverse proxy if your network
> is not fully trusted.

## Management API

```bash
curl -X POST http://127.0.0.1:9697/api/overrides \
  -H 'Content-Type: application/json' \
  -d '{"tvdbId": 81189, "source": "tmdb", "tmdbId": 1396}'

curl http://127.0.0.1:9697/api/overrides          # list overrides
curl -X DELETE http://127.0.0.1:9697/api/overrides/81189
```

`tmdbId` is optional; without it the mapping is looked up automatically.

## Build / publish (for maintainers)

```bash
git push origin develop   # CI: tests + publish as :develop (Docker Hub + GHCR, amd64+arm64)
git tag v0.2.4 && git push origin v0.2.4   # CI: tests + publish as :0.2.4 (single tag)
```

The workflow pushes to `crisperfx/sonarr-metadata-proxy` (Docker Hub) and
`ghcr.io/<owner>/sonarr-metadata-proxy` (GHCR). For that, set the `DOCKERHUB_USERNAME` and
`DOCKERHUB_TOKEN` secrets in the repo (Settings → Secrets) (Personal Access Token with
Read/Write on the Docker Hub repo); GHCR works with the standard `GITHUB_TOKEN` and needs no
setup. Without `DOCKERHUB_*` secrets only the Docker Hub push fails, the GHCR push succeeds.

Tags by branch/ref (one build, one tag per event — no `sha-...` or minor-version tags):
- `develop` → published only as `:develop` (clearly a dev build, never `latest`).
- `main` → published only as `:latest`.
- `vX.Y.Z` → published only as `:X.Y.Z` (e.g. `git tag v1.1.4` → image `crisperfx/sonarr-metadata-proxy:1.1.4`).

Local testing: `dotnet test` or via Docker: `docker compose build sonarr-metadata-proxy`.

## Limitations

- AniList search filters to `format: [TV, TV_SHORT]` (excludes movies, specials, OVAs). Anime without a known TVDB mapping get a synthetic TVDB ID and appear in search; details/episodes served via TMDB (synthetic → TMDB). On empty results or API errors, falls back to TVDB.
- MAL search filters to `type: tv` (excludes movies, OVAs, music, etc.). Series without a known TVDB mapping get a synthetic TVDB ID; on empty results or API errors, falls back to TVDB.
- Episodes of series without a TVDB mapping get proxy-local (stable) episode ids.
- TMDB has no air time, so `timeOfDay` is missing.
