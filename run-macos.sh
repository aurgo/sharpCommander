#!/bin/bash
# Creates a temporary .app bundle and launches SharpCommander with a proper
# macOS Dock icon. This is needed because 'dotnet run' doesn't create a
# .app bundle, and macOS requires one for the Dock icon.
#
# The trick: the native .NET host (SharpCommander.Desktop) is a Mach-O binary.
# By symlinking the entire output dir as Contents/MacOS, macOS launches the
# native host FROM WITHIN the bundle, so NSBundle.mainBundle reads our
# Info.plist and icon.icns correctly.

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/src/SharpCommander.Desktop"
OUTPUT_DIR="$PROJECT_DIR/bin/Debug/net8.0"
BUNDLE_DIR="/tmp/SharpCommander.app"

# Build first
echo "Building SharpCommander..."
dotnet build "$PROJECT_DIR/SharpCommander.Desktop.csproj"

# Copy icns to output if not there
if [ ! -f "$OUTPUT_DIR/icon.icns" ] && [ -f "$PROJECT_DIR/Resources/icon.icns" ]; then
    cp "$PROJECT_DIR/Resources/icon.icns" "$OUTPUT_DIR/icon.icns"
fi

# Create .app bundle structure
rm -rf "$BUNDLE_DIR"
mkdir -p "$BUNDLE_DIR/Contents/Resources"

# Copy Info.plist and icon
cp "$OUTPUT_DIR/Info.plist" "$BUNDLE_DIR/Contents/Info.plist"
cp "$OUTPUT_DIR/icon.icns" "$BUNDLE_DIR/Contents/Resources/icon.icns"

# Symlink the entire output directory as MacOS so the native host
# runs from within the bundle (NSBundle.mainBundle works correctly)
ln -sf "$OUTPUT_DIR" "$BUNDLE_DIR/Contents/MacOS"

echo "Launching SharpCommander.app..."
open -a "$BUNDLE_DIR" --wait
