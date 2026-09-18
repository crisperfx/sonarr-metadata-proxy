#!/usr/bin/env bash
# Verification without a local .NET SDK: compiles + runs all tests on the server
# using a throwaway .NET 8 SDK container. Run this in the project root on the
# Docker host (your server).
#
#   ./scripts/verify-on-server.sh

set -euo pipefail
cd "$(dirname "$0")/.."

echo "==> Building metadata proxy (docker build runs 'dotnet publish')"
docker compose build sonarr-metadata-proxy

echo "==> Running tests in a throwaway SDK container"
docker run --rm \
    -v "$(pwd):/src" \
    -w /src \
    mcr.microsoft.com/dotnet/sdk:8.0 \
    dotnet test tests/Sonarr.MetadataProxy.Tests/Sonarr.MetadataProxy.Tests.csproj -v minimal

echo "==> All checks passed."