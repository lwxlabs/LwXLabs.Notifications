#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACTS="$ROOT/artifacts/release"
mkdir -p "$ARTIFACTS"
cd "$ROOT"

dotnet restore LwXLabs.Notifications.sln
dotnet build LwXLabs.Notifications.sln -c Release --no-restore
dotnet test tests/LwXLabs.Notifications.Tests/LwXLabs.Notifications.Tests.csproj -c Release --no-build --no-restore

for project in \
  src/LwXLabs.Notifications.Abstractions/LwXLabs.Notifications.Abstractions.csproj \
  src/LwXLabs.Notifications/LwXLabs.Notifications.csproj \
  src/LwXLabs.Notifications.SignalR.Client/LwXLabs.Notifications.SignalR.Client.csproj \
  src/LwXLabs.Notifications.AspNetCore/LwXLabs.Notifications.AspNetCore.csproj \
  src/LwXLabs.Notifications.Firebase/LwXLabs.Notifications.Firebase.csproj; do
  dotnet pack "$project" -c Release --no-build --no-restore -o "$ARTIFACTS"
done

echo "Packages: $ARTIFACTS"
