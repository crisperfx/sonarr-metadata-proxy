# Project rules

## Pushing / deploying
- ALWAYS ask before pushing, tagging, or bumping the version. Never push on your own.
- Ask whether the image needs to be rebuilt/deployed at all. If the change is local-only (e.g. a UI tweak that will be synced by hand and no image rebuild is needed), DO NOT push until the user confirms.
- When the user asks to push a release, default to: bump `<Version>` in `src/Sonarr.MetadataProxy/Sonarr.MetadataProxy.csproj` and `version` in `src/Sonarr.MetadataProxy/Program.cs`, then commit, push, tag `vX.Y.Z`, and push the tag (README documents this flow).
- The user runs deploy steps on the NAS themselves (pull image, restart `tester`/`Sonarr`).

## Conventions
- Do not add comments to code unless asked.
- Confirm behavior with the user after deploy; CI runs the tests (no local .NET SDK on this machine).