#!/bin/bash
# Builds SharpCommander (Debug) and launches it from a temporary .app bundle so macOS shows the proper Dock
# icon and application name. 'dotnet run' does not create a bundle, and macOS reads Info.plist and icon.icns
# only from one.
#
# The trick: the native .NET host (SharpCommander.Desktop) is a Mach-O binary. By symlinking the entire
# output directory as Contents/MacOS, macOS launches the native host FROM WITHIN the bundle, so
# NSBundle.mainBundle reads our Info.plist and icon.icns correctly.
#
# Release builds for distribution are made by publish.sh, which produces a real SharpCommander.app.

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/src/SharpCommander.Desktop"
PROJECT_FILE="$PROJECT_DIR/SharpCommander.Desktop.csproj"

# The target framework comes from the project file so this script does not go stale when it changes.
TFM=$(grep -o '<TargetFramework>[^<]*</TargetFramework>' "$PROJECT_FILE" | head -n 1 | sed -E 's#</?TargetFramework>##g' || true)
if [ -z "$TFM" ]; then
    echo "ERROR: no <TargetFramework> element found in $PROJECT_FILE" >&2
    exit 1
fi

OUTPUT_DIR="$PROJECT_DIR/bin/Debug/$TFM"
BUNDLE_DIR="${TMPDIR:-/tmp}/SharpCommander.app"

# Build first
echo "Building SharpCommander ($TFM)..."
dotnet build "$PROJECT_FILE"

# Create .app bundle structure
rm -rf "$BUNDLE_DIR"
mkdir -p "$BUNDLE_DIR/Contents/Resources"

# Info.plist and icon come straight from the project
cp "$PROJECT_DIR/Info.plist" "$BUNDLE_DIR/Contents/Info.plist"
cp "$PROJECT_DIR/Resources/icon.icns" "$BUNDLE_DIR/Contents/Resources/icon.icns"

# Symlink the entire output directory as MacOS so the native host
# runs from within the bundle (NSBundle.mainBundle works correctly)
ln -sf "$OUTPUT_DIR" "$BUNDLE_DIR/Contents/MacOS"

echo "Launching SharpCommander.app..."
open -a "$BUNDLE_DIR" --wait
