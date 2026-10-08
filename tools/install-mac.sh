#!/bin/bash
set -euo pipefail
PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE="$PROJECT_DIR/artifacts/Miche.app"
DESTINATION="$HOME/Applications/Miche.app"
if [ ! -d "$SOURCE" ]; then echo "Build Miche first with tools/build-mac.sh"; exit 1; fi
mkdir -p "$HOME/Applications"
if [ -e "$DESTINATION" ]; then
  if [ "$(/usr/bin/plutil -extract CFBundleIdentifier raw "$DESTINATION/Contents/Info.plist" 2>/dev/null || true)" != "local.miche.mac.preview" ]; then
    echo "Destination already exists and is not this managed Miche preview. Kept it intact."; exit 1
  fi
  if /bin/ps -axo comm= | /usr/bin/grep -Fq "$DESTINATION/Contents/MacOS/Miche"; then
    echo "Miche is running from Applications. Quit it normally before installing an update."; exit 1
  fi
fi
STAGE="$(mktemp -d "$HOME/Applications/.miche-install.XXXXXX")"
/usr/bin/ditto "$SOURCE" "$STAGE/Miche.app"
if [ -e "$DESTINATION" ]; then mv "$DESTINATION" "$HOME/Applications/Miche.previous-$(date +%Y%m%d-%H%M%S)-$$.app"; fi
mv "$STAGE/Miche.app" "$DESTINATION"
rmdir "$STAGE"
echo "Installed: $DESTINATION"
echo "Quit your previous preview normally, then open this app. Data remains in Application Support/Miche.Mac."
