#!/bin/bash
set -euo pipefail
PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
MEILI_SDK="${MICHE_MAC_SDK:-$(xcrun --sdk macosx --show-sdk-path)}"
# This machine's default 27 SDK has newer stub architectures than its linker.
if [ -z "${MICHE_MAC_SDK:-}" ] && [ -d /Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk ]; then
  MEILI_SDK=/Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk
fi
clang -isysroot "$MEILI_SDK" -dynamiclib -fobjc-arc -framework AppKit -framework WebKit \
  -mmacosx-version-min=13.0 "$PROJECT_DIR/native/meili-webview.m" -o "$PROJECT_DIR/native/libmeili-webview.dylib"
