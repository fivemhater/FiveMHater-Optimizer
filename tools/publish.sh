#!/usr/bin/env bash
# Linux/macOS üzerinde çapraz derleme (EnableWindowsTargeting=true csproj içinde tanımlı)
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet publish FiveMHaterOptimizer/FiveMHaterOptimizer.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o ./publish
ls -la ./publish/FiveMHaterOptimizer.exe
