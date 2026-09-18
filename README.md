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

### Dockhand / Portainer (image pullen) — stap voor stap

Voor wie alleen een image wil pullen, geen repo/downloads. De UI toont env/ports niet
vooraf ingevuld (zo werken Docker-UIs als Dockhand), maar dat is niet erg: **alle defaults
zitten al in de image**, en de Sonarr-injectiebestanden zitten óók in de image — bij de
eerste start legt de proxy ze in z'n data-map.

**Stap 0 — Vooraf (eenmalig)**

- TMDB API-key: <https://www.themoviedb.org/settings/api>
- Maak een map op je data-volume waar de proxy zijn gegevens bewaart, bijvoorbeeld:
  `/volume3/docker/config/sonarr-metadata-proxy`
  (Dit ís "het volume": Sonarr bindt straks dezelfde map.)

**Stap 1 — Nieuwe container `sonarr-metadata-proxy`**

- Image: `crisperfx/sonarr-metadata-proxy:latest`
- **Start de container eerst helemaal blanco** en wacht tot hij UP is. Bij de eerste start
  draait de image met de ingebakken defaults en worden de injectiebestanden + de CA in de
  map gelegd.

**Stap 2 — Proxy stoppen en dan pas invullen**

Stop de container, open daarna de configuratie en vul het volgende in:

| Veld | Waarde |
|---|---|
| Naam | `sonarr-metadata-proxy` |
| Port mapping (optioneel) | `9697:9697` — alleen als je `/info` buiten Docker wilt bereiken |
| Environment variable | `TMDB_API_KEY` = `<jouw key>` |
| Environment variable | `CORS_ALLOWED_ORIGINS` = `http://<sonarr-ip>:8989` — proxy etc. mogelijk, scheiden met een "," |
| Volume (host-map → container) | `/volume3/docker/config/sonarr-metadata-proxy` → `/app/data` |
| Extra capability | `NET_BIND_SERVICE` — nodig om poort 443 te binden |

> Gebruik je liever een named volume in plaats van een host-map? Maak er dan één aan
> (bijv. `sonarr-metadata-proxy`) en gebruik datzelfde volume bij beide containers.

Na (opnieuw) starten ligt in die map onder andere: `01-install-ca.sh`,
`50-sonarr-override-ui.sh`, `metadata-proxy-override.js` en `certs/ca.crt`.

**Stap 3 — Sonarr aanpassen (nieuw óf bestaand)**

Voor een nieuwe Sonarr gebruik je gewoon `lscr.io/linuxserver/sonarr:latest`. Bij een
bestaande Sonarr: open de configuratie en voeg de velden hieronder toe.

| Veld | Waarde |
|---|---|
| Volume | `/volume3/docker/config/sonarr-metadata-proxy` → `/shared` (read-only) |
| Volume | `/volume3/docker/config/sonarr-metadata-proxy` → `/custom-cont-init.d` (read-only) |

Netwerk & DNS (belangrijk): Sonarr moet `skyhook.sonarr.tv` bij de proxy laten
landen (poort 443 in Docker).

- Zet beide containers op **hetzelfde netwerk**. Een apart netwerk is niet nodig: de
  standaard **bridge** werkt prima, containers bereiken elkaar dan via hun IP.
- Geef Sonarr daarvoor een **hosts-entry**: key `skyhook.sonarr.tv`, value = het IP van de
  `sonarr-metadata-proxy`-container (staat in de containerdetails van stap 1; dat IP blijft
  gelijk zolang die container niet opnieuw wordt aangemaakt).
- Liever een vast netwerk? Maak er dan één aan en zet beide containers erop — het IP blijft
  dan ook stabiel.

**Stap 4 — Beide herstarten (in deze volgorde)**

1. Start/herstart de proxy (`sonarr-metadata-proxy`) en wacht tot hij helemaal UP is;
2. Start Sonarr — hij installeert bij de start de CA en patcht zijn eigen web-UI
   (logregel `[sonarr-metadata-proxy] index.html patched...`);
3. **Herstart Sonarr daarna nog één keer** — nu bestaat `config.xml`, dus wordt je Sonarr
   API-key in de dropdown gestopt en verschijnt er nooit een key-prompt.

**Stap 5 — Testen**

- Sonarr-log: `[sonarr-metadata-proxy] Installing proxied CA for skyhook.sonarr.tv`
  en `index.html patched with override UI script`.
- Sonarr openen (poort `8989`) → **Add Series** → zoeken → resultaten uit TMDB.
- Seriepagina openen → "Metadata-bron"-dropdown → **TMDB** kiezen → **Refresh & Scan**.

**Stap 6 — Als je de proxy ooit opnieuw aanmaakt**

- Gebruik exact dezelfde map/volume → mappings en CA blijven bewaard;
- het IP kan dan veranderen → werk de hosts-entry bij Sonarr bij (of herstart beide
  zonder opnieuw op te bouwen).

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
