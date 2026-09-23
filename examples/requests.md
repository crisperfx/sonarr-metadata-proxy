# Example requests

All examples call the proxy exactly the way Sonarr would. SkyHook-language is `en`.
`9697` is the management port (plain HTTP); the TLS listener is on `443`.

## Search series by title (same call Sonarr makes for the UI search box)

```bash
curl "http://127.0.0.1:9697/v1/tvdb/search/en/?term=breaking%20bad"
```

## Lookup by TMDB id (Sonarr sends the prefixed term)

```bash
curl "http://127.0.0.1:9697/v1/tvdb/search/en/?term=tmdb%3A1396"
```

## Lookup by IMDb id

```bash
curl "http://127.0.0.1:9697/v1/tvdb/search/en/?term=imdb%3Att0903747"
```

## Lookup by TVDB id (local + reverse-mapped, never forwarded to TMDB search)

```bash
curl "http://127.0.0.1:9697/v1/tvdb/shows/en/81189"
```

Anime terms — `anilist:1535` resolves through AniList; `mal:1535` resolves
through MAL (Tenrai) when mapping data is available:

```bash
curl "http://127.0.0.1:9697/v1/tvdb/search/en/?term=anilist%3A1535"
curl "http://127.0.0.1:9697/v1/tvdb/search/en/?term=mal%3A1535"
```

## Full series object with episodes (what Sonarr fetches on add + every refresh)

```bash
# real TVDB id
curl "http://127.0.0.1:9697/v1/tvdb/shows/en/81189"

# synthetic id (series without a TVDB mapping), see 1000001396-response.json
curl "http://127.0.0.1:9697/v1/tvdb/shows/en/1000001396"
```

## Management

```bash
curl "http://127.0.0.1:9697/health"     # {"status":"ok"}
curl "http://127.0.0.1:9697/info"       # {"assembly":"...","version":"...","source":"tmdb","name":"Sonarr Metadata Proxy"}
```

## Direct end-to-end TLS check (what trust the CA enables)

```bash
curl --cacert ../data/certs/ca.crt "https://skyhook.sonarr.tv/v1/tvdb/search/en/?term=breaking%20bad"
```

Expected error responses:

| Case | HTTP | Body |
|---|---|---|
| Series without mapping, fallback disabled | `404` | `{"error":"mapping unavailable"}` |
| Source API down, fallback disabled | `404` (shows) / `503` (search) | `{"error":"metadata source unavailable"}` |
| Fallback enabled and backend down | `503` | raw backend body |