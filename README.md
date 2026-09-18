# Sonarr Metadata Proxy

Sidecar die een **ongewijzigde Sonarr** van metadata uit **TMDB** voorziet door transparant
Sonarrs metadata-aanvragen (`skyhook.sonarr.tv` / TVDB) te ondervangen en terug te
vertalen naar het exacte JSON-contract dat Sonarr verwacht. Geen fork, geen patched
Sonarr, geen lokale .NET SDK nodig — draait als prebuilt Docker-image.

## Werkende functies

- Sonarr zoeken + toevoegen + refreshen volledig via **TMDB** (series, seizoenen, episodes).
- Correcte **airdates** (`airDateUtc`) uit TMDB in Sonarr.
- Afbeeldingen, acteurs, ratings, genres, netwerk, status uit TMDB.
- **Per-serie bronkeuze TMDB/TVDB**: dropdown in elke Sonarr-seriepagina (de zgn.
  "Metadata-bron") óf via een kleine REST-API (`/api/overrides`).
- **Automatische TVDB↔TMDB-mapping** (TMDB `external_ids` + Wikidata reverse lookup),
  persistent opgeslagen zodat refreshes stabiele ids teruggeven.
- Series zonder TVDB-mapping krijgen een **stabiel synthetisch TVDB-id**.
- **TVDB-fallback** wanneer een serie niet te mappen is of de bron faalt.
- Prebuilt image op **Docker Hub** en **GHCR** (spiegel) voor `linux/amd64` en `linux/arm64`;
  CI draait de test-suite en publiceert een image bij elke `v*`-tag.

## Vereisten

- Docker met Compose v2.
- Gratis TMDB API-key: <https://www.themoviedb.org/settings/api>.

## Installatie (docker compose)

```bash
git clone https://github.com/crisperfx/sonarr-metadata-proxy.git
cd sonarr-metadata-proxy
cp .env.example .env
# .env openen: alleen TMDB_API_KEY en CORS_ALLOWED_ORIGINS invullen
docker compose up -d
```

- De compose gebruikt `crisperfx/sonarr-metadata-proxy` (Docker Hub). Gebruik je liever de
  GHCR-spiegel, vervang dan de image door `ghcr.io/crisperfx/sonarr-metadata-proxy`, of
  comment `image:` uit en activeer `build: .` om lokaal te bouwen.
- De compose start een schone Sonarr (poort `8989`) plus de proxy (poort `9697`). De
  proxy antwoordt op `skyhook.sonarr.tv` (netwerk-alias) en regelt zelf een CA-certificaat
  dat Sonarr automatisch installeert.
- Open Sonarr → **Add Series** → zoeken → toevoegen. Alles komt uit TMDB.

### Losse/draaiende Sonarr gebruiken

Voeg aan je bestaande Sonarr-service drie dingen toe: de `certs`-volume, de
`init/01-install-ca.sh`-mount naar `/custom-cont-init.d/`, en het alias-netwerk. Zie de
`sonarr:` service in `docker-compose.yml` als voorbeeld. Verder niets aan Sonarr wijzigen.

## Per-serie bronkeuze ("Metadata-bron")

Op elke seriepagina staat nu een dropdown: **Automatisch / TMDB / TVDB**.

- Automatisch = de standaardbron uit `METADATA_SOURCE`.
- TMDB = deze serie altijd uit TMDB servers.
- TVDB = deze serie altijd via de echte SkyHook/TVDB.

Na een keuze: **Refresh & Scan** op de serie in Sonarr. Overrides worden in
`mappings.json` bewaard. De eerste keer wordt naar je Sonarr API-key gevraagd
(Settings → General); die wordt automatisch uit `/config/config.xml` gelezen zodra
`init/50-sonarr-override-ui.sh` draait, dus meestal is er geen prompt.

## Environment variabelen

| Variabele | Default | Omschrijving |
|---|---|---|
| `METADATA_SOURCE` | `tmdb` | Hoofdbron: `tmdb` of `tvdb` (alleen passthrough). |
| `TMDB_API_KEY` | – | TMDB v3 API-key (verplicht voor TMDB). |
| `TMDB_API_TOKEN` | – | TMDB v4 bearer token, alternatief voor de key. |
| `TMDb_LANGUAGE` | `en-US` | Taal voor TMDB-aanvragen. |
| `ENABLE_TVDB_FALLBACK` | `true` | Bij mapping-fout/bron-faal doorvallen naar echte TVDB. |
| `PORT` | `9697` | HTTP-poort voor beheer/health; de SkyHook/TLS-luisteraar zit altijd op `443`. |
| `SKIP_TLS` | `false` | `true` = TLS/443 uit (alleen voor dev, niet met Sonarr). |
| `SKYHOOK_BASE_URL` | `https://skyhook.sonarr.tv` | Echte SkyHook voor de TVDB-fallback. |
| `SKYHOOK_RESOLVER_URL` | `https://cloudflare-dns.com/dns-query` | DNS-over-HTTPS voor de fallback-host. |
| `CORS_ALLOWED_ORIGINS` | leeg | Browser-origins die `/api/overrides` mogen aanroepen (nodig voor de dropdown). Meerdere met komma's, `*` = alles. |
| `DATA_DIR` | `/app/data` | Map voor mappings + CA/certificaten (volume in Docker). |

Deze waarden zet je in `.env`, of als environment op de container / in je eigen compose.

## Beheer-API

```bash
curl -X POST http://127.0.0.1:9697/api/overrides \
  -H 'Content-Type: application/json' \
  -d '{"tvdbId": 81189, "source": "tmdb", "tmdbId": 1396}'

curl http://127.0.0.1:9697/api/overrides          # lijst van overrides
curl -X DELETE http://127.0.0.1:9697/api/overrides/81189
```

`tmdbId` is optioneel; zonder wordt de mapping automatisch opgezocht.

## Builden / publiceren (voor maintainer)

```bash
git tag v0.2.2 && git push origin v0.2.2   # trekt CI: tests + publish naar Docker Hub en GHCR (amd64+arm64)
```

De workflow pusht naar `crisperfx/sonarr-metadata-proxy` (Docker Hub) en
`ghcr.io/<owner>/sonarr-metadata-proxy` (GHCR). Zet daarvoor in de repo
(Settings → Secrets) de secrets `DOCKERHUB_USERNAME` en `DOCKERHUB_TOKEN` (Personal
Access Token met Read/Write op het Docker Hub repo); GHCR werkt met de standaard
`GITHUB_TOKEN` en hoeft niet ingesteld te worden. Zonder `DOCKERHUB_*`-secrets mislukt
alleen de Docker Hub push, de GHCR-push slaagt gewoon.

Lokaal testen: `dotnet test` of via Docker: `docker compose build sonarr-metadata-proxy`.

## Limitaties

- `anilist:`/`mal:` zoektermen vallen nog door naar TVDB.
- Episodes van series zonder TVDB-mapping krijgen proxy-lokale (stabiele) episode-ids.
- TMDB heeft geen uitzendtijd, dus `timeOfDay` ontbreekt.