#!/bin/bash

# SharpCommander - macOS installer.
#
# SharpCommander is signed ad-hoc but not notarized: notarization needs a paid Apple Developer account. macOS
# therefore quarantines it on download and refuses to run it, which for a bare binary means being killed with no
# message at all. This script removes that quarantine flag from THIS copy and puts the application in place.
#
# It is deliberately small and readable: it asks you to lower a security check, so you should be able to read
# exactly what it does before running it. It verifies the application's signature first, so it will not strip the
# flag from something that is not the application that shipped in this archive.

set -euo pipefail

APP_NAME="SharpCommander.app"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE="$SCRIPT_DIR/$APP_NAME"
TARGET_DIR="/Applications"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

echo ""
echo -e "${CYAN}SharpCommander - installer${NC}"
echo ""

if [ ! -d "$SOURCE" ]; then
    echo -e "${RED}ERROR: $APP_NAME is not next to this script.${NC}" >&2
    echo "Extract the whole archive first, then run this script from the folder it created." >&2
    exit 1
fi

# The signature is checked before anything is touched: this script exists to lower a protection, so it should at
# least confirm it is lowering it for the application that came with it.
echo "Checking the signature..."
if ! codesign --verify --deep --strict "$SOURCE" 2>/dev/null; then
    echo -e "${RED}ERROR: the signature of $APP_NAME does not verify.${NC}" >&2
    echo "Do not install this copy. Download it again from:" >&2
    echo "  https://github.com/aurgo/sharpCommander/releases" >&2
    exit 1
fi
echo -e "${GREEN}  The signature is valid (ad-hoc).${NC}"

# Where to install: /Applications when it is writable, the user's own folder otherwise. No sudo is asked for.
if [ ! -w "$TARGET_DIR" ]; then
    TARGET_DIR="$HOME/Applications"
    mkdir -p "$TARGET_DIR"
    echo -e "${YELLOW}  /Applications is not writable; installing into $TARGET_DIR instead.${NC}"
fi

TARGET="$TARGET_DIR/$APP_NAME"

if [ -e "$TARGET" ]; then
    read -r -p "$APP_NAME is already in $TARGET_DIR. Replace it? [y/N] " answer
    case "$answer" in
        [yY]|[yY][eE][sS]) ;;
        *) echo "Nothing was changed."; exit 0 ;;
    esac

    # A running copy cannot be replaced cleanly.
    if pgrep -f "$TARGET/Contents/MacOS/" >/dev/null 2>&1; then
        echo -e "${RED}ERROR: SharpCommander is running. Quit it and run this again.${NC}" >&2
        exit 1
    fi

    rm -rf "$TARGET"
fi

echo "Copying to $TARGET_DIR..."
# ditto, not cp: it keeps the bundle's structure and its signature intact.
ditto "$SOURCE" "$TARGET"

echo "Removing the download quarantine flag..."
xattr -dr com.apple.quarantine "$TARGET" 2>/dev/null || true

echo ""
echo -e "${GREEN}Installed: $TARGET${NC}"
echo ""
echo "Open it from Launchpad, or with:"
echo "  open \"$TARGET\""
echo ""
