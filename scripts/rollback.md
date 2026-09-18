# Rollback instructions - Sonarr Metadata Proxy

The proxy never modifies Sonarr's source code, binary, database or configuration.
Reverting to the original Sonarr behaviour is a two-step process.

## 1. Stop using the proxy

### Docker Compose demo
```bash
docker compose down
```
Remove the `sonarr-data`, `tv` and `downloads` bind mounts only if you also want to
delete your Sonarr data. The `certs` and `proxy-data` volumes can be removed with:

```bash
docker volume rm sonarr-metadata-proxy_certs sonarr-metadata-proxy_proxy-data
```

### Existing Sonarr pointed at the proxy
- Remove the network alias for `skyhook.sonarr.tv` from the proxy's compose service.
- Remove the mounted CA trust script and cert volume from the Sonarr container.
- Remove `docker compose up -d sonarr-metadata-proxy` (or `docker stop sonarr-metadata-proxy`),
  or just stop the proxy container.

(An `extra_hosts` entry for `skyhook.sonarr.tv` is no longer used; the fallback
resolves the real address at runtime via DNS-over-HTTPS.)

## 2. Restore certificate validation options

### If you used the CA install script (recommended)
The CA is only trusted while the init script + cert volume are mounted. Once you
remove them, next Sonarr container starts use their normal trust store.

Optionally verify: open Sonarr -> System -> Logs, add a series or trigger a search;
it should now hit the real `skyhook.sonarr.tv` with regular certificate validation.

### If you used Settings -> General -> Certificate Validation -> Disabled
This is Sonarr's own built-in setting. Restore it to **Enabled**:

1. Sonarr UI -> Settings -> General -> Security.
2. Set *Certificate Validation* back to **Enabled**.
3. Save.

## 3. What happens to series added while the proxy was active

- Series added with a **real TVDB id** (the common case: TMDB exposes a `tvdb_id`
  for most shows) continue to work perfectly after rollback, because Sonarr stored
  the real TVDB id.
- Series added **without** a TVDB mapping received a *synthetic* TVDB id
  (`tvdbId >= 1.000.000.000`). After rollback the real TVDB backend will not know
  those ids, so refreshing such series will report "series not found". To fix,
  re-add a series manually: look it up, add it, then delete the old one (your
  download history stays intact because it is keyed by path/previously downloaded).

This is inherent to any sidecar approach: the proxy works by satisfying the SkyHook
contract from its own metadata sources, so IDs that never existed on TVDB cannot be
looked up once the proxy is gone.