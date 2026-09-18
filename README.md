<img width="1172" height="764" alt="image" src="https://github.com/user-attachments/assets/529787c7-3703-44bf-a076-c2a1daee2a20" />

# Sonarr Metadata Proxy

A sidecar that feeds an **unmodified Sonarr** with metadata from **TMDB** by transparently
intercepting Sonarr's metadata requests (`skyhook.sonarr.tv` / TVDB) and translating them
back into the exact JSON contract Sonarr expects. No fork, no patched Sonarr, no local
.NET SDK required — runs as a prebuilt Docker image.

## Working features

- Sonarr search + add + refresh fully via **TMDB** (series, seasons, episodes).
- Correct **airdates** (`airDateUtc`) from TMDB into Sonarr.
- Images, actors, ratings, genres, network, status from TMDB.
- **Per-series TMDB/TVDB source selection**: a dropdown on every Sonarr series page
  (the "Metadata source" picker) or via a small REST API (`/api/overrides`).
- **Automatic TVDB↔TMDB mapping** (TMDB `external_ids` + Wikidata reverse lookup),
  persisted so refreshes return stable ids.
- Series without a TVDB mapping get a **stable synthetic TVDB id**.
- **TVDB fallback** when a series cannot be mapped or the source fails.
- Prebuilt image on **Docker Hub** and **GHCR** (mirror) for `linux/amd64` and `linux/arm64`;
  CI runs the test suite and publishes an image on every `v*` tag.

## Requirements

- Docker with Compose v2.
- A free TMDB API key: <https://www.themoviedb.org/settings/api>.

## Installation (docker compose)

```bash
git clone https://github.com/crisperfx/sonarr-metadata-proxy.git
cd sonarr-metadata-proxy
cp .env.example .env
# open .env: only TMDB_API_KEY and CORS_ALLOWED_ORIGINS need your own values
docker compose up -d
```

- The compose file uses `crisperfx/sonarr-metadata-proxy` (Docker Hub). Prefer the GHCR
  mirror? Replace the image with `ghcr.io/crisperfx/sonarr-metadata-proxy`, or comment out
  `image:` and enable `build: .` to build locally.
- The compose starts a clean Sonarr (port `8989`) plus the proxy (port `9697`). The proxy
  answers on `skyhook.sonarr.tv` (network alias) and manages its own CA certificate that
  Sonarr installs automatically.
- Open Sonarr → **Add Series** → search → add. Everything comes from TMDB.

### Dockhand / Portainer (image pull) — step by step

For those who only want to pull the image, no repo/downloads involved. Docker UIs such as
Dockhand do not show env/ports pre-filled, but that is fine: **all defaults are already in
the image**, and the Sonarr injection files are in the image too — the proxy drops them
into its data directory on first start.

**Step 0 — Beforehand (one-time)**

- TMDB API key: <https://www.themoviedb.org/settings/api>
- Create a folder on your data volume where the proxy keeps its data, for example:
  `/volume3/docker/config/sonarr-metadata-proxy`
  (This *is* "the volume": Sonarr will bind the same folder.)

**Step 1 — Create the `sonarr-metadata-proxy` container**

- Image: `crisperfx/sonarr-metadata-proxy:latest`
- **Start the container completely blank first** and wait until it is UP. On first start the
  image runs with its baked-in defaults and places the injection files + the CA in the folder.

**Step 2 — Stop the proxy, then fill in the settings**

Stop the container, then open its configuration and fill in the following:

| Field | Value |
|---|---|
| Name | `sonarr-metadata-proxy` |
| Port mapping (optional) | `9697:9697` — only if you want `/info` reachable outside Docker |
| Environment variable | `TMDB_API_KEY` = `<your key>` |
| Environment variable | `CORS_ALLOWED_ORIGINS` = `http://<sonarr-ip>:8989` — multiple allowed, separate with "," |
| Volume (host path → container) | `/volume3/docker/config/sonarr-metadata-proxy` → `/app/data` |
| Extra capability | `NET_BIND_SERVICE` — required to bind port 443 |

> Prefer a named volume instead of a host path? Create one (e.g. `sonarr-metadata-proxy`)
> and use the same volume on both containers.

After (re)starting, that folder contains among others: `01-install-ca.sh`,
`50-sonarr-override-ui.sh`, `metadata-proxy-override.js` and `certs/ca.crt`.

**Step 3 — Configure Sonarr (new or existing)**

For a new Sonarr just use `lscr.io/linuxserver/sonarr:latest`. For an existing Sonarr:
open its configuration and add the fields below.

| Field | Value |
|---|---|
| Volume | `/volume3/docker/config/sonarr-metadata-proxy` → `/shared` (read-only) |
| Volume | `/volume3/docker/config/sonarr-metadata-proxy` → `/custom-cont-init.d` (read-only) |
| Environment variable (optional) | `OVERRIDES_API_URL` = `https://proxy.example.com` — only behind a reverse proxy |

Network & DNS (important): Sonarr must make `skyhook.sonarr.tv` land on the proxy
(port 443 in Docker).

- Put both containers on **the same network**. A separate network is not required: the
  default **bridge** works fine; containers reach each other via their IP.
- Give Sonarr a **hosts entry** for that: key `skyhook.sonarr.tv`, value = the IP of the
  `sonarr-metadata-proxy` container (shown in the container details of step 1; that IP stays
  the same as long as that container is not recreated).
- Prefer a fixed network? Create one and put both containers on it — the IP stays stable too.

**Step 4 — Restart both (in this order)**

1. Start/restart the proxy (`sonarr-metadata-proxy`) and wait until it is fully UP;
2. Start Sonarr — on startup it installs the CA and patches its own web UI
   (log line `[sonarr-metadata-proxy] index.html patched...`);
3. **Restart Sonarr once more** — now `config.xml` exists, so your Sonarr API key is
   embedded in the dropdown and there will never be a key prompt.

**Step 5 — Testing**

- Sonarr log: `[sonarr-metadata-proxy] Installing proxied CA for skyhook.sonarr.tv`
  and `index.html patched with override UI script`.
- Open Sonarr (port `8989`) → **Add Series** → search → results from TMDB.
- Open a series page → "Metadata source" dropdown → choose **TMDB** → **Refresh & Scan**.

**Step 6 — If you ever recreate the proxy**

- Use exactly the same folder/volume → mappings and CA stay preserved;
- the IP may change then → update the Sonarr hosts entry (or restart both without
  recreating).

### Behind an HTTPS reverse proxy (e.g. Synology DSM)

If you open Sonarr as `https://sonarr.example.com` instead of `http://<ip>:8989`, the
"Metadata source" dropdown does not work with `CORS_ALLOWED_ORIGINS` alone. The dropdown JS
talks to `http://<host>:9697` by default, which is blocked behind HTTPS (mixed content) and
port 9697 is never forwarded by the reverse proxy.

So put the management API behind the reverse proxy too:

1. **New reverse proxy rule** in DSM → Login Portal → Advanced → Reverse Proxy:
   - `https://proxy.example.com` → `http://<metadata-proxy-ip>:9697`
   - Use a *different* subdomain; DSM cannot route two backends on one hostname.
2. **`OVERRIDES_API_URL`** goes on the **Sonarr** container (env):
   - `OVERRIDES_API_URL=https://proxy.example.com`
   - The init hook (`init/50-sonarr-override-ui.sh`) embeds this into the UI JS; recreate
     Sonarr so the patch runs again.
3. **`CORS_ALLOWED_ORIGINS`** on the proxy container stays the **browser origin of Sonarr**:
   - `CORS_ALLOWED_ORIGINS=https://sonarr.example.com`
4. Test: series page → dropdown → **TMDB** → **Refresh & Scan**.

Without `OVERRIDES_API_URL` the JS falls back to `http://<host>:9697` (LAN/port-forward);
that keeps working for local use.

### Using a standalone/existing Sonarr

Add three things to your existing Sonarr service: the `certs` volume, the
`init/01-install-ca.sh` mount into `/custom-cont-init.d/`, and the alias network. Use the
`sonarr:` service in `docker-compose.yml` as an example. Nothing else in Sonarr changes.

## Per-series source selection ("Metadata source")

Every series page now has a dropdown: **Automatic / TMDB / TVDB**.

- Automatic = the default source from `METADATA_SOURCE`.
- TMDB = always use TMDB servers for this series.
- TVDB = always use the real SkyHook/TVDB for this series.

After choosing: **Refresh & Scan** on the series in Sonarr. Overrides are stored in
`mappings.json`. The first time you will be asked for your Sonarr API key
(Settings → General); it is read automatically from `/config/config.xml` once
`init/50-sonarr-override-ui.sh` runs, so usually there is no prompt.

## Environment variables

| Variable | Default | Description |
|---|---|---|
| `METADATA_SOURCE` | `tmdb` | Primary source: `tmdb` or `tvdb` (passthrough only). |
| `TMDB_API_KEY` | – | TMDB v3 API key (required for TMDB). |
| `TMDB_API_TOKEN` | – | TMDB v4 bearer token, alternative to the key. |
| `TMDb_LANGUAGE` | `en-US` | Language for TMDB requests. |
| `ENABLE_TVDB_FALLBACK` | `true` | Fall back to the real TVDB on mapping/source failure. |
| `PORT` | `9697` | HTTP port for management/health; the SkyHook/TLS listener is always on `443`. |
| `SKIP_TLS` | `false` | `true` = TLS/443 disabled (dev only, not with Sonarr). |
| `SKYHOOK_BASE_URL` | `https://skyhook.sonarr.tv` | Real SkyHook used for the TVDB fallback. |
| `SKYHOOK_RESOLVER_URL` | `https://cloudflare-dns.com/dns-query` | DNS-over-HTTPS for the fallback host. |
| `CORS_ALLOWED_ORIGINS` | empty | Browser origins allowed to call `/api/overrides` (needed for the dropdown). Multiple with commas, `*` = all. |
| `DATA_DIR` | `/app/data` | Directory for mappings + CA/certificates (volume in Docker). |

Set these in `.env`, or as environment on the container / in your own compose.

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
git tag v0.2.2 && git push origin v0.2.2   # triggers CI: tests + publish to Docker Hub and GHCR (amd64+arm64)
```

The workflow pushes to `crisperfx/sonarr-metadata-proxy` (Docker Hub) and
`ghcr.io/<owner>/sonarr-metadata-proxy` (GHCR). For that, set the `DOCKERHUB_USERNAME` and
`DOCKERHUB_TOKEN` secrets in the repo (Settings → Secrets) (Personal Access Token with
Read/Write on the Docker Hub repo); GHCR works with the standard `GITHUB_TOKEN` and needs no
setup. Without `DOCKERHUB_*` secrets only the Docker Hub push fails, the GHCR push succeeds.

Local testing: `dotnet test` or via Docker: `docker compose build sonarr-metadata-proxy`.

## Limitations

- `anilist:`/`mal:` search terms still pass through to TVDB.
- Episodes of series without a TVDB mapping get proxy-local (stable) episode ids.
- TMDB has no air time, so `timeOfDay` is missing.
