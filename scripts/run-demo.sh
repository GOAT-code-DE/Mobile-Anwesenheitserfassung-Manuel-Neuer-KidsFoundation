#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5080}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet run --project src/NeuerKids/NeuerKids.csproj
