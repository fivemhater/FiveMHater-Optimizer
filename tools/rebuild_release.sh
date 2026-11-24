#!/usr/bin/env bash
# Kullanım: tools/rebuild_release.sh [/yol/gercek_logo.gif]
# Logo verilirse Assets yeniden üretilir; ardından exe + kaynak zip + rehber release klasörüne yazılır.
set -euo pipefail
cd "$(dirname "$0")/.."
SRC=$(pwd)
REL=/workspace/fivemhater-optimizer-release
export PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
[ $# -ge 1 ] && tools/make_assets.sh "$1"
rm -rf FiveMHaterOptimizer/bin FiveMHaterOptimizer/obj /tmp/fhpub
(cd FiveMHaterOptimizer && dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true \
   -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o /tmp/fhpub)
mkdir -p "$REL"
cp /tmp/fhpub/FiveMHaterOptimizer.exe "$REL/"
cp DERLEME_VE_IMZALAMA.md "$REL/"
rm -rf /tmp/zipstage && mkdir -p /tmp/zipstage && cp -r "$SRC" /tmp/zipstage/FiveMHaterOptimizer-source
rm -rf /tmp/zipstage/FiveMHaterOptimizer-source/FiveMHaterOptimizer/{bin,obj}
rm -f "$REL/FiveMHaterOptimizer-source.zip"
(cd /tmp/zipstage && zip -qr "$REL/FiveMHaterOptimizer-source.zip" FiveMHaterOptimizer-source)
file "$REL/FiveMHaterOptimizer.exe"; ls -la "$REL"
