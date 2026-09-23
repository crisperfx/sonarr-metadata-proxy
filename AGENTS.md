# Project rules

## Pushing / deploying
- ALWAYS ask before pushing, tagging, or bumping the version. Never push on your own.
- Ask whether the image needs to be rebuilt/deployed at all. If the change is local-only (e.g. a UI tweak that will be synced by hand and no image rebuild is needed), DO NOT push until the user confirms.
- When the user asks to push a release, default to: bump `<Version>` in `src/Sonarr.MetadataProxy/Sonarr.MetadataProxy.csproj` and `version` in `src/Sonarr.MetadataProxy/Program.cs`, then commit, push, tag `vX.Y.Z`, and push the tag (README documents this flow).
- The user runs deploy steps on the NAS themselves (pull image, restart `tester`/`Sonarr`).

## Conventions
- Do not add comments to code unless asked.
- Confirm behavior with the user after deploy; CI runs the tests (no local .NET SDK on this machine).

## Adding a new search provider (checklist)
Missed touchpoints here are a recurring bug class — update ALL of these, not just the client:
- `src/Sonarr.MetadataProxy/Mapping/MappingStore.cs`:
  - add `SourceXxx` string constant;
  - add `TryGetXxxIdByTvdb` / `RegisterXxxId` (that use the same persistence as AniList/MAL);
  - extend `PersistedState` dictionaries + serialization;
  - extend source validation in `SetOverride` / `SetDefaultSearchSource`;
  - clear the new association too in `RemoveOverride`.
- `src/Sonarr.MetadataProxy/Controllers/OverridesController.cs`: extend the `request.Source` whitelist check.
- `src/Sonarr.MetadataProxy/Services/TermClassifier.cs`: add the `xxx:` prefix + `TermKind`.
- `src/Sonarr.MetadataProxy/Services/MetadataRequestHandler.cs`: search dispatch per prefix + default search source; if the provider is anime-bound, also update `FlattenIfAnimeBound` — an override of `SourceAniList`/`SourceMal` counts as anime-bound even when there is no binding (searchs must serve a single continuous season in all three paths: mapped TMDB, passthrough TVDB, and override-without-binding).
- `src/Sonarr.MetadataProxy/Services/XxxSearchService.cs`: search + by-id lookup; call `RegisterXxxId` when a show resolves to a TVDB id.
- `src/Sonarr.MetadataProxy/Providers/XxxClient.cs` + interface: HTTP client, timeout, user-agent, rate limiting.
- `src/Sonarr.MetadataProxy/Translation/XxxTranslator.cs`: convert to `ShowResource`/`SeriesMetadata`.
- `src/Sonarr.MetadataProxy/MetadataProviderRegistry.cs` (or equivalent): add to the `Create` switch.
- `src/Sonarr.MetadataProxy/Program.cs`: DI registration (`AddHttpClient<IXxxApi, XxxClient>`).
- `init/metadata-proxy-override.js`: add the provider to BOTH dropdown option lists (series picker + Add-New picker) AND to `normalizeSearchSource`.
- Tests: `tests/Sonarr.MetadataProxy.Tests/Infrastructure/Fakes.cs`, `TestData.cs`, plus provider/client/search-service/handler/mapping tests, incl. a flatten test for an override without binding.
- Docs: `README.md` (provider table, env var table, per-series dropdown bullets), `.env.example`, `examples/requests.md`.