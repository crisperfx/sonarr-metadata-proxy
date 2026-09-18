# Sonarr Metadata Proxy

A standalone sidecar that lets an **unmodified, stock Sonarr** resolve metadata from
**TMDB** (and later AniList / MyAnimeList / IMDb) by transparently answering Sonarr's
SkyHook metadata requests. Ships as a prebuilt Docker image on **GitHub Container
Registry** — no local .NET SDK, no patched Sonarr, no source build required.

**No fork. No patched binary. No source changes. No recompilation.** The stock Sonarr
container/package keeps running; the proxy sits in front of the metadata backend and
translates the responses back into the exact JSON contract Sonarr expects.

```
┌────────────┐    skyhook.sonarr.tv     ┌─────────────────────┐         ┌──────────┐
│   Sonarr   │ ───────────────────────▶ │ Metadata Proxy      │  ─────▶ │  TMDB    │
│ (unchanged)│ ◀─────────────────────── │ SkyHook-compatible  │  ◀───── │  (or an  │
└────────────┘   Sonarr-compatible JSON │ endpoint (proxy)    │         │  alt src)│
                                        └─────────────────────┘
```

---

## 1. How Sonarr currently fetches metadata (research summary)

This project is based on the **current Sonarr codebase** (v4/develop). Sonarr's whole
metadata path is one small, hardcoded surface:

| Fact | Detail | Source (Sonarr repo) |
|---|---|---|
| Metadata base URL (hardcoded, no config option) | `https://skyhook.sonarr.tv/v1/tvdb/{route}/{language}/` with `language=en` | `src/NzbDrone.Common/Cloud/SonarrCloudRequestBuilder.cs` |
| Series detail endpooint | `GET /v1/tvdb/shows/en/{tvdbId}` → single `ShowResource` incl. *all episodes* | `SkyHookProxy.GetSeriesInfo` |
| Search endpoint | `GET /v1/tvdb/search/en/?term={lowercased term}` → `List<ShowResource>` (no episodes) | `SkyHookProxy.SearchForNewSeries` |
| Lookup-by-id terms | `tmdb:123`, `imdb:tt123`, `anilist:123`, `mal:123`, `tvdb:123`/`tvdbid:123` | `SkyHookProxy` (by-id helpers prefix the term) |
| Auth | None – plain GET, no API key, no header | `HttpRequestBuilder` defaults |
| TLS trust | Normal OS trust store, **no certificate pinning**, redirects allowed | `X509CertificateValidationService` |
| Retries | None; search errors surface as `SkyHookException` (HTTP 503), `shows` 404 → `SeriesNotFoundException` | `SkyHookProxy`, `HttpClient` |

What happens per feature:

- **Series search** (UI search box): Sonarr calls the SkyHook `search` endpoint.
- **Lookup by ID** (`tmdb:`, `imdb:`, `anilist:`, `mal:`, `tvdb:`): the same search
  endpoint receives the prefixed term. `tvdb:`/`tvdbid:` terms are resolved locally and
  via the `shows` endpoint.
- **Add series**: Sonarr calls `shows/{tvdbId}` after `POST /api/v3/series`.
- **Refresh series / metadata refresh / season refresh / episode refresh**: all go
  through `shows/{tvdbId}` (Sonarr stores the TVDB id and re-fetches the full object
  with all episodes).
- **Images / alternate titles**: embedded in `ShowResource` (`images`, `seasons`/`images`,
  `alternativeTitles`).

**Fields Sonarr reads off `ShowResource`** (via `SkyHookProxy.MapSeries` /
`MapEpisode`): `tvdbId` (int, mandatory), `title`, `overview`, `firstAired`/`lastAired`
(`yyyy-MM-dd` or null), `originalLanguage`, `status` (`ended`/`upcoming`/anything else
→ continuing), `runtime`, `network`, `timeOfDay`, `slug`, `contentRating`, `rating
{value,count}`, `genres`, `tvRageId`, `tvMazeId`, `tmdbId`, `imdbId`, `malIds`,
`aniListIds`, `actors[]`, `images[]`, `seasons[] {seasonNumber, images[]}` and for the
detail endpoint `episodes[]` (`tvdbId`, `title`, `overview`, `seasonNumber`,
`episodeNumber`, `airDate`, `airDateUtc`, ...). Every list field must always be present
as an array.

## 2. Is external interception actually possible?

**Yes** – and it is proven (the same technique powers the open-source Glossarr proxy).
Three facts make it work without touching Sonarr:

1. The SkyHook base URL is **compiled in**, but it resolves like any hostname, so a
   DNS/hosts override is sufficient – Sonarr has no pinned IP.
2. Sonarr does **not** pin certificates. It uses .NET's normal trust store, so a
   locally generated CA installed into the container makes Sonarr accept the proxy's
   certificate for `skyhook.sonarr.tv`.
3. The whole surface Sonarr ever talks to is **exactly two GET endpoints** and one
   well-defined JSON contract (see above).

### What cannot be done externally

- **No per-series "metadata source" picker *built into* the Sonarr UI.** A UI selector
  in Sonarr would require Sonarr changes. The proxy solves this at the *backend*
  instead: the configured source (`METADATA_SOURCE`) decides what the metadata calls
  actually return, a **per-series override** can pin an individual real TVDB id to
  `tmdb` or `tvdb` through a management API (see below), and a small JS injection
  (`init/50-sonarr-override-ui.sh`) turns that into a dropdown on each series page.
- **Existing series added before the proxy** have a real TVDB id. On refresh the proxy
  tries to map real TVDB ↔ TMDB (via TMDB `external_ids`, the local mapping store, and
  a Wikidata reverse lookup for previously unknown ids) and otherwise falls back to the
  real SkyHook. So existing data is never damaged, but it may continue to refresh from
  TVDB until its mapping is recorded.
- **New series added through search always come from the active source.** They get a
  synthetic TVDB id in Sonarr, so a later "switch this one series to the other source"
  is only possible for series that already carry a *real* TVDB id (see override API).

### Per-series source override (management API)

The proxy listens for management traffic on `PORT` (default `9697`). Three endpoints
manage a per-real-TVDB-id override (`tvdb` → always served from the real SkyHook,
`tmdb` → always resolved to TMDB). Example: pin series **81189** (Breaking Bad) to TMDB
order and record its mapping in one call:

```bash
curl -X POST http://127.0.0.1:9697/api/overrides \
  -H 'Content-Type: application/json' \
  -d '{"tvdbId": 81189, "source": "tmdb", "tmdbId": 1396}'

# list overrides
curl http://127.0.0.1:9697/api/overrides
# {"tvdbId":81189,"source":"tmdb","tmdbId":1396}

# force a series back to TVDB order
curl -X POST http://127.0.0.1:9697/api/overrides \
  -H 'Content-Type: application/json' \
  -d '{"tvdbId": 81189, "source": "tvdb"}'

# back to default (active source) behaviour
curl -X DELETE http://127.0.0.1:9697/api/overrides/81189
```

After setting an override, refresh the series in Sonarr (`Series → ... → Refresh &
Scan`) and the proxy re-serves the series from the chosen source. `tmdbId` is optional;
when omitted for `tmdb`, the proxy tries to resolve the TVDB→TMDB mapping (Wikidata)
before applying the override. Overrides persist in `DATA_DIR/mappings.json`.

#### In-Sonarr picker (GUI injection)

The same override API is exposed in the Sonarr web UI itself. `init/50-sonarr-override-ui.sh`
(second `/custom-cont-init.d` hook) copies `init/metadata-proxy-override.js` into the
Sonarr UI and patches `index.html`, so a **Metadata-bron** dropdown shows on every
series detail page: *Automatisch / TMDB / TVDB*. On change it writes to the override
API; then use **Refresh & Scan** for the new order to apply.

Requirements:
- The two files must be mounted into `/custom-cont-init.d/` (see compose wiring above).
- The proxy must advertise `CORS_ALLOWED_ORIGINS` with the **exact origin of your
  browser tab** (scheme+host+port), and port `9697` must be reachable from the browser
  (open in the firewall if the UI is opened from another machine).
- The first time the panel appears it asks for the Sonarr **API key** (Settings →
  General). It is stored in `localStorage` of your browser only — it never leaves the
  browser and is never sent to the proxy.

## 3. Design

```
                    ┌──────────────────┐
                    │      Sonarr      │  (unchanged)
                    └────────┬─────────┘
                             ▼
              https://skyhook.sonarr.tv:443   (DNS alias / hosts override)
                 ┌──────────────────────┐
                 │  SkyHookController   │  GET /v1/tvdb/search|shows/{language}/...
                 └──────────┬───────────┘
                            ▼
                 MetadataRequestHandler
                            │
                     MetadataProviderRegistry        ┌─ enable/disable per source
                            │                        ▼
              IMetadataProvider (active)  ──  TmdbMetadataProvider  (POC)
                            │
                 ┌──────────┼──────────┐
                 ▼          ▼          ▼
           SkyHookTranslator   SkyHookPassthrough   (TVDB fallback → real SkyHook)
                 └──────────┬──────────┘
                            ▼
                    MappingStore / SyntheticIds / WikidataTvdbResolver
```

Interfaces follow the requested shape; only TMDB is fully implemented in this POC:

```csharp
public interface IMetadataProvider
{
    string Name { get; }
    IReadOnlyList<SeriesMetadata> Search(string query, CancellationToken ct);
    IReadOnlyList<SeriesMetadata> SearchById(string providerId, CancellationToken ct);
    IReadOnlyList<SeriesMetadata> SearchByImdbId(string imdbId, CancellationToken ct);
    SeriesMetadata GetSeries(string providerId, CancellationToken ct);
    IReadOnlyList<SeasonMetadata> GetSeasons(string providerId, CancellationToken ct);
}
```

Integrations planned to implement this interface: `TvdbMetadataProvider` (passthrough),
`TmdbMetadataProvider` ✅, `AniListMetadataProvider`, `MalMetadataProvider`,
`ImdbMetadataProvider`.

### ID mapping (critical)

Sonarr is TVDB-id-centric. The proxy therefore keeps a persistent mapping layer:

- **TMDB → TVDB**: fetched from TMDB `external_ids` (most series have a real `tvdb_id`).
- **No mapping exists**: the proxy synthesises a stable id from a private namespace
  (`tvdbId = 1_000_000_000 + tmdb`) and stores the TMDB id so lookups and refreshes keep
  working. These ids only exist while the proxy is active (see `scripts/rollback.md`).
- **TVDB → TMDB** (for pre-existing series): local mapping store first, then a read-only
  Wikidata reverse lookup (`P12196` → `P4985`), then TVDB fallback.
- **Episodes** also get stable synthetic ids (`>= 180_000_000`), persisted per
  `(series, season, episode)` so refreshes return identical ids.
- The mapping store is a JSON file (`DATA_DIR/mappings.json`) on a persistent volume.
- If a mapping truly cannot be determined: **mapping unavailable** → controlled fallback
  (or `404`/`503` when fallback is disabled). No made-up TVDB ids are ever invented.

## 4. Quick start (Docker demo, includes a Sonarr)

Requirements: Docker with Compose v2, a TMDB API key (free):
<https://www.themoviedb.org/settings/api>.

```bash
git clone https://github.com/crisperfx/sonarr-metadata-proxy.git
cd sonarr-metadata-proxy
cp .env.example .env
# edit .env: set TMDB_API_KEY=..., CORS_ALLOWED_ORIGINS = the origin of your Sonarr tab
docker compose up -d
```

The compose file pulls the prebuilt image `ghcr.io/crisperfx/sonarr-metadata-proxy:latest`;
replace `USERNAME` with the GitHub account that owns the repository. To build locally
instead of pulling, comment out the `image:` line and uncomment the `build: .` line in
`docker-compose.yml`.

The proxy generates a development CA + server certificate on first start and writes
them to the shared `certs` volume. Sonarr's container installs the CA via
`/custom-cont-init.d/01-install-ca.sh` (the LinuxServer init hook) and then talks to
the proxy because of the network alias `skyhook.sonarr.tv` on the proxy container.

The per-series **Metadata-bron** picker (Automatisch / TMDB / TVDB) is injected on every
Sonarr series page by the second init hook. Flip the dropdown, press **Refresh & Scan**
and Sonarr re-serves that series from the chosen source.

Verify:

```bash
# proxy management
curl http://127.0.0.1:9697/info

# search translated the same way Sonarr would call it (plain http, dev mode only if SKIP_TLS=true)
curl "http://127.0.0.1:9697/v1/tvdb/search/en/?term=breaking%20bad"
```

Then open Sonarr at http://localhost:8989 → **Add Series** → search for a show. Searches
and additions are answered from TMDB; a TVDB mapping is logged:

```
[INFO] Incoming Sonarr metadata request: series search, term 'breaking bad'.
[INFO] Source: TMDB. TMDB result count: 2.
[INFO] TVDB mapping found for TMDB 1396: TVDB 81189.
[INFO] Returning Sonarr-compatible metadata.
```

## 5. Pointing an *existing* Sonarr installation at the proxy

Because Sonarr's SkyHook URL is fixed, the only thing needed is a name→address override
**plus** cert trust. No Sonarr config is changed.

### 5a. Existing Docker Sonarr (recommended)

Add the proxy to your existing compose and wire the alias, the shared cert volume and
the CA install script, e.g.:

```yaml
services:
  sonarr:
    # ... your existing sonarr service, plus:
    volumes:
      - certs:/shared/certs:ro                       # add
      - ./init/01-install-ca.sh:/custom-cont-init.d/01-install-ca.sh:ro   # add
      - ./init/50-sonarr-override-ui.sh:/custom-cont-init.d/50-sonarr-override-ui.sh:ro   # add (GUI picker)
      - ./init:/shared/init:ro                       # add (GUI picker JS; NOT in /custom-cont-init.d, else it is executed as a script)
    networks:
      - arrnet                                       # add

  sonarr-metadata-proxy:
    image: ghcr.io/crisperfx/sonarr-metadata-proxy:latest
    environment:
      METADATA_SOURCE: tmdb
      TMDB_API_KEY: ${TMDB_API_KEY}
      CORS_ALLOWED_ORIGINS: http://192.168.1.10:8989   # add: exact origin your browser uses for Sonarr
    volumes:
      - proxy-data:/app/data
      - certs:/app/data/certs
    networks:
      arrnet:
        aliases:
          - skyhook.sonarr.tv                        # embedded DNS answers the name
    restart: unless-stopped

volumes:
  certs: {}
  proxy-data: {}

networks:
  arrnet: {}
```

Start Sonarr once **after** the proxy has created the CA (the compose `depends_on:
service_healthy` takes care of ordering).

### 5b. Native (non-Docker) Sonarr

1. Run the proxy wherever you like (Docker or `dotnet run`), with `SKIP_TLS=true` and a
   LAN-bound port, **or** with TLS and the CA installed on the host.
2. Add an entry to the Sonarr host's `/etc/hosts` (or a DNS override):
   ```
   192.168.x.y   skyhook.sonarr.tv
   ```
3. Trust the CA (`/app/data/certs/ca.crt`) in the OS trust store, or set Sonarr
   Settings → General → **Certificate Validation → Disabled** (built-in; also used for
   quick POC testing). Then restart Sonarr.

Rollback is the reverse: remove the hosts override, uninstall the CA (or re-enable
certificate validation), stop the proxy. See `scripts/rollback.md`.

## 6. Configuration reference (environment variables)

| Variable | Default | Description |
|---|---|---|
| `METADATA_SOURCE` | `tmdb` | Active metadata source. `tvdb` = pure passthrough. `anilist`/`mal`/`imdb` are accepted; not implemented yet (requests fall back to TVDB). |
| `TMDB_API_KEY` | – | TMDB v3 API key. |
| `TMDB_API_TOKEN` | – | TMDB v4 bearer token (used instead of the key when set). |
| `TMDb_LANGUAGE` | `en-US` | Language passed to TMDB. |
| `ENABLE_TVDB_FALLBACK` | `true` | When a mapping fails, the API is down, or the source is unimplemented, fall back to the real SkyHook/TVDB backend. |
| `LOG_LEVEL` | `Information` | Serilog level. API keys are **never** logged. |
| `PORT` | `9697` | Management/health HTTP port. The SkyHook TLS listener is always `443`. |
| `CACHE_TTL_MINUTES` | `1440` | Memory cache TTL (reserved for a later cache implementation). |
| `SEARCH_RESULT_LIMIT` | `10` | Max results a title search translates from TMDB. |
| `SKIP_TLS` | `false` | Disables the 443 TLS listener (dev/testing only; Sonarr always uses HTTPS). |
| `SKYHOOK_BASE_URL` | `https://skyhook.sonarr.tv` | Real SkyHook base URL used only by the TVDB fallback/passthrough. |
| `SKYHOOK_RESOLVER_URL` | `https://cloudflare-dns.com/dns-query` | DNS-over-HTTPS endpoint used to resolve the fallback host. Needed because the Docker network alias answers for `skyhook.sonarr.tv`; it avoids both a self-loop and a hardcoded edge IP. |
| `CORS_ALLOWED_ORIGINS` | – | Comma-separated browser origins allowed to call `/api/overrides` (needed for the injected Sonarr-GUI picker). E.g. `http://192.168.1.10:8989`. `*` allows any. Empty disables CORS. |
| `DATA_DIR` | `/app/data` | Mapping store + generated CA/certificates. Persistent volume in Docker. |
| `ANILIST_ENABLED`, `MAL_ENABLED`, `IMDB_ENABLED` | `false` | Reserved for the next phases. |

## 7. Testing

No local .NET SDK needed: the quickest check is the Docker build itself (its SDK
stage runs `dotnet publish`, so any compile error fails the image build). To also run
the full test suite on the Docker host without installing a SDK:

```bash
sudo docker compose build sonarr-metadata-proxy   # compiles the service
sudo docker run --rm -v "$(pwd):/src" -w /src \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test tests/Sonarr.MetadataProxy.Tests/Sonarr.MetadataProxy.Tests.csproj
```

Or use the ready-made wrapper: `./scripts/verify-on-server.sh`.

With a local .NET 8 SDK: `dotnet test` from the repository root.

Coverage includes: term classification (`tvdb:`/`tmdb:`/`imdb:`/`mal:`/`anilist:`),
synthetic-id synthesis and inversion, mapping-store persistence, TMDB→metadata mapping,
SkyHook JSON contract (field presence, date format, arrays never null), episode-id
stability, and integration tests exercising the real HTTP endpoints with faked TMDB /
resolver / passthrough backends, including failure and fallback paths.

## 8. Publishing & CI (GitHub Actions → GHCR)

A GitHub Actions workflow (`.github/workflows/ci.yml`) runs the test suite on every push
and PR. On pushes to `main` and on `v*` tags it builds the image for **linux/amd64** and
**linux/arm64** and pushes it to **GHCR** as `ghcr.io/<owner>/sonarr-metadata-proxy`
using the repository's own `GITHUB_TOKEN` — no extra credentials needed.

```bash
git tag v0.2.2 && git push origin v0.2.2   # builds and publishes ghcr.io/<owner>/sonarr-metadata-proxy:v0.2.2
```

Tags are published as `v0.2.2`, `0.2.2`, `0.2`, the branch name, and a commit SHA. Always
tag-and-push to release. To also mirror to Docker Hub, add a `docker/login-action` step
with `DOCKERHUB_USERNAME`/`DOCKERHUB_TOKEN` secrets and a second `images:` entry.

## 9. Repository layout

```
sonarr-metadata-proxy/
├── src/Sonarr.MetadataProxy/          # service (ASP.NET Core / .NET 8)
│   ├── Controllers/                   # SkyHook-compatible endpoints
│   ├── Services/                      # request pipeline + provider registry
│   ├── Providers/                     # IMetadataProvider + TMDB implementation
│   ├── Translation/                   # TMDB/SkyHook contract mapping
│   ├── Mapping/                       # persistent id mapping + synthetic ids
│   ├── Reverse/                       # Wikidata TVDB→TMDB resolver
│   ├── Passthrough/                   # TVDB fallback (real SkyHook)
│   ├── Contracts/SkyHook/             # the ShowResource/EpisodeResource contract
│   ├── Options/                       # env config
│   └── Tls/                           # dev CA + server certificate generation
├── tests/Sonarr.MetadataProxy.Tests/
├── Dockerfile
├── docker-compose.yml
├── .env.example
├── init/01-install-ca.sh              # LinuxServer /custom-cont-init.d CA hook
├── examples/                          # sample requests/responses
└── scripts/                           # test + rollback helpers
```

## 10. Limitations & roadmap

- Search results come from TMDB only in this POC; `anilist:`/`mal:` terms fall through
  to TVDB.
- TMDB has no `timeOfDay`, so `airDateUtc`/`timeOfDay` are omitted (Sonarr imports fine
  on season+episode numbers).
- Episode ids for shows without a TVDB mapping are proxy-local, not real TVDB ids.
- Recommended next steps: `AniListMetadataProvider` (GraphQL), `MalMetadataProvider`
  (jikan/official API), `ImdbMetadataProvider` (official datasets) and response
  caching.