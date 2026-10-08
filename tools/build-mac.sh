#!/bin/bash
set -euo pipefail
PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
SDK="$PROJECT_DIR/.tools/dotnet/dotnet"
if [ ! -x "$SDK" ]; then
  SDK="$(command -v dotnet || true)"
fi
if [ -z "$SDK" ]; then
  echo "Install a .NET 8 SDK, or use tools/dotnet-install.sh --channel 8.0 --architecture arm64 --install-dir .tools/dotnet --no-path"
  exit 1
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
APP="$PROJECT_DIR/artifacts/Miche.app"
mkdir -p "$PROJECT_DIR/artifacts"
STAGING_ROOT="$(mktemp -d "$PROJECT_DIR/artifacts/miche-build.XXXXXX")"
STAGED_APP="$STAGING_ROOT/Miche.app"
mkdir -p "$STAGED_APP/Contents/MacOS" "$STAGED_APP/Contents/Resources"
"$SDK" publish "$PROJECT_DIR/Miche.Mac.csproj" -c Release -r osx-arm64 --self-contained true -p:UseAppHost=true -o "$STAGED_APP/Contents/MacOS"
cp "$PROJECT_DIR/assets/Miche.icns" "$STAGED_APP/Contents/Resources/Miche.icns"
cp "$PROJECT_DIR/tools/Info.plist" "$STAGED_APP/Contents/Info.plist"
chmod +x "$STAGED_APP/Contents/MacOS/Miche"
if [ -d "$APP" ]; then
  mv "$APP" "$PROJECT_DIR/artifacts/Miche.previous-$(date +%Y%m%d-%H%M%S)-$$.app"
fi
mv "$STAGED_APP" "$APP"
rmdir "$STAGING_ROOT"
echo "Built: $APP"
