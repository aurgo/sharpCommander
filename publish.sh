#!/bin/bash

# SharpCommander - publish script for Linux and macOS.
#
# A thin wrapper over 'dotnet publish'. The publish configuration (self-contained, partial trimming, no debug
# symbols) lives in src/SharpCommander.Desktop/SharpCommander.Desktop.csproj so every platform and every script
# ships the same binaries, and the version is read from Directory.Build.props. macOS platforms are wrapped in a
# SharpCommander.app bundle (ad-hoc signed when codesign is available) and the bundle is what gets zipped.

set -eo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Configuration
PROJECT_DIR="src/SharpCommander.Desktop"
PROJECT_PATH="$PROJECT_DIR/SharpCommander.Desktop.csproj"
PROPS_FILE="Directory.Build.props"
OUTPUT_BASE="publish"
APP_NAME="SharpCommander"
EXECUTABLE="SharpCommander.Desktop"
ALL_PLATFORMS=(win-x64 win-x86 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64)

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

print_header() {
    echo ""
    echo -e "${CYAN}============================================${NC}"
    echo -e "${CYAN}  SharpCommander - Build and Publish Script${NC}"
    echo -e "${CYAN}============================================${NC}"
    echo ""
}

print_usage() {
    echo "Usage: $0 [OPTIONS] [PLATFORM...]"
    echo ""
    echo "Platforms (one or more; default: all):"
    echo "  win-x64      Windows x64"
    echo "  win-x86      Windows x86"
    echo "  win-arm64    Windows ARM64"
    echo "  linux-x64    Linux x64"
    echo "  linux-arm64  Linux ARM64"
    echo "  osx-x64      macOS x64 (Intel)"
    echo "  osx-arm64    macOS ARM64 (Apple Silicon)"
    echo "  all          All platforms"
    echo ""
    echo "Options:"
    echo "  --no-zip     Skip creating ZIP archives (ZIPs are created by default)"
    echo "  -a, --aot    Native AOT compilation (-p:PublishAot=true; needs the platform toolchain, no cross-OS builds)"
    echo "  -h, --help   Show this help"
    echo ""
    echo "The publish settings come from $PROJECT_PATH and the version from $PROPS_FILE."
    echo "macOS platforms produce $OUTPUT_BASE/<rid>/$APP_NAME.app; the other platforms produce loose files."
    echo ""
    echo "Examples:"
    echo "  $0 linux-x64"
    echo "  $0 osx-arm64 osx-x64"
    echo "  $0 all --no-zip"
}

# Reads the <Version> element of Directory.Build.props, the single source of the version.
read_version() {
    local version
    version=$(grep -o '<Version>[^<]*</Version>' "$PROPS_FILE" | head -n 1 | sed -E 's#</?Version>##g' | tr -d '[:space:]' || true)
    if [ -z "$version" ]; then
        echo -e "${RED}ERROR: no <Version> element found in $PROPS_FILE${NC}" >&2
        return 1
    fi
    echo "$version"
}

is_known_platform() {
    local candidate=$1
    local rid
    for rid in "${ALL_PLATFORMS[@]}"; do
        if [ "$rid" = "$candidate" ]; then
            return 0
        fi
    done
    return 1
}

# Completes the bundle whose Contents/MacOS folder has just been published: Info.plist from the project with
# the version patched in, the icon in Contents/Resources, and an ad-hoc signature when codesign exists.
finish_app_bundle() {
    local app=$1
    local contents="$app/Contents"
    local macos="$contents/MacOS"

    mkdir -p "$contents/Resources"

    sed -e "/<key>CFBundleVersion<\/key>/{n;s#<string>[^<]*</string>#<string>$VERSION</string>#;}" \
        -e "/<key>CFBundleShortVersionString<\/key>/{n;s#<string>[^<]*</string>#<string>$VERSION</string>#;}" \
        "$PROJECT_DIR/Info.plist" > "$contents/Info.plist" || return 1
    cp "$PROJECT_DIR/Resources/icon.icns" "$contents/Resources/icon.icns" || return 1

    # The csproj copies both files next to the binaries (for 'dotnet run'); the bundle keeps them in their place.
    rm -f "$macos/Info.plist" "$macos/Resources/icon.icns"
    rmdir "$macos/Resources" 2>/dev/null || true
    chmod +x "$macos/$EXECUTABLE"

    if command -v codesign >/dev/null 2>&1; then
        if codesign --force --deep -s - "$app" 2>/dev/null; then
            echo -e "  ${CYAN}Ad-hoc signed: $app${NC}"
        else
            echo -e "  ${YELLOW}WARNING: codesign failed, the bundle is left unsigned${NC}"
        fi
    fi
}

# Zips the publish folder: the .app bundle for macOS (ditto keeps the bundle intact), the loose files otherwise.
create_zip() {
    local rid=$1
    local output_dir=$2
    local zip_path="$SCRIPT_DIR/$3"

    rm -f "$zip_path"

    case "$rid" in
        osx-*)
            if command -v ditto >/dev/null 2>&1; then
                ditto -c -k --norsrc --keepParent "$output_dir/$APP_NAME.app" "$zip_path"
            else
                (cd "$output_dir" && zip -r -y -q "$zip_path" "$APP_NAME.app")
            fi
            ;;
        *)
            (cd "$output_dir" && zip -r -q "$zip_path" .)
            ;;
    esac
}

publish_platform() {
    local rid=$1
    local output_dir="$OUTPUT_BASE/$rid"
    local publish_dir="$output_dir"
    local artifact="$output_dir/$EXECUTABLE"

    echo -e "${YELLOW}[Building for $rid...]${NC}"

    case "$rid" in
        osx-*)
            publish_dir="$output_dir/$APP_NAME.app/Contents/MacOS"
            artifact="$output_dir/$APP_NAME.app"
            ;;
        win-*)
            artifact="$output_dir/$EXECUTABLE.exe"
            ;;
    esac

    # Start from a clean folder so files of an earlier publish never end up in the archive.
    rm -rf "$output_dir"
    mkdir -p "$publish_dir"

    # The csproj supplies the publish settings; only configuration, runtime and output are passed here.
    local publish_args=(publish "$PROJECT_PATH" -c Release -r "$rid" -o "$publish_dir")
    if [ "$AOT" = true ]; then
        publish_args+=(-p:PublishAot=true)
        echo -e "  ${CYAN}AOT compilation enabled${NC}"
    fi

    if ! dotnet "${publish_args[@]}"; then
        echo -e "  ${RED}ERROR: Build failed for $rid${NC}"
        return 1
    fi

    case "$rid" in
        osx-*)
            if ! finish_app_bundle "$output_dir/$APP_NAME.app"; then
                echo -e "  ${RED}ERROR: The .app bundle could not be completed for $rid${NC}"
                return 1
            fi
            ;;
    esac

    if [ ! -e "$artifact" ]; then
        echo -e "  ${RED}ERROR: Expected output $artifact was not produced${NC}"
        return 1
    fi

    local size
    size=$(du -sh "$artifact" | cut -f1)
    echo -e "  ${GREEN}SUCCESS: $artifact ($size)${NC}"

    if [ "$CREATE_ZIP" = true ]; then
        local zip_name="$APP_NAME-v$VERSION-$rid.zip"
        local zip_path="$OUTPUT_BASE/$zip_name"

        echo -e "  ${CYAN}Creating ZIP: $zip_name${NC}"
        if ! create_zip "$rid" "$output_dir" "$zip_path"; then
            echo -e "  ${RED}ERROR: The ZIP could not be created for $rid${NC}"
            return 1
        fi

        local zip_size
        zip_size=$(du -h "$zip_path" | cut -f1)
        echo -e "  ${GREEN}ZIP created: $zip_path ($zip_size)${NC}"
    fi

    return 0
}

# Default values
PLATFORMS=()
CREATE_ZIP=true
AOT=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --no-zip)
            CREATE_ZIP=false
            ;;
        -a|--aot)
            AOT=true
            ;;
        -h|--help)
            print_usage
            exit 0
            ;;
        all)
            PLATFORMS=("${ALL_PLATFORMS[@]}")
            ;;
        *)
            if is_known_platform "$1"; then
                PLATFORMS+=("$1")
            else
                echo -e "${RED}Unknown option or platform: $1${NC}"
                print_usage
                exit 1
            fi
            ;;
    esac
    shift
done

if [ ${#PLATFORMS[@]} -eq 0 ]; then
    PLATFORMS=("${ALL_PLATFORMS[@]}")
fi

# Main execution
print_header

if ! command -v dotnet >/dev/null 2>&1; then
    echo -e "${RED}ERROR: The .NET SDK ('dotnet') was not found on the PATH${NC}"
    exit 1
fi

VERSION=$(read_version) || exit 1
echo -e "${CYAN}Version $VERSION (from $PROPS_FILE)${NC}"

# Create output directory
mkdir -p "$OUTPUT_BASE"

SUCCESSFUL=()
FAILED=()

for rid in "${PLATFORMS[@]}"; do
    echo ""
    if publish_platform "$rid"; then
        SUCCESSFUL+=("$rid")
    else
        FAILED+=("$rid")
    fi
done

# Summary
echo ""
echo -e "${CYAN}============================================${NC}"
echo -e "${CYAN}  Build Summary${NC}"
echo -e "${CYAN}============================================${NC}"
echo ""

if [ ${#SUCCESSFUL[@]} -gt 0 ]; then
    echo -e "${GREEN}Successful builds (${#SUCCESSFUL[@]}):${NC}"
    for rid in "${SUCCESSFUL[@]}"; do
        echo -e "  ${GREEN}- $rid${NC}"
    done
fi

if [ ${#FAILED[@]} -gt 0 ]; then
    echo ""
    echo -e "${RED}Failed builds (${#FAILED[@]}):${NC}"
    for rid in "${FAILED[@]}"; do
        echo -e "  ${RED}- $rid${NC}"
    done
fi

echo ""
echo -e "${CYAN}Output directory: $OUTPUT_BASE${NC}"

if [ "$CREATE_ZIP" = true ]; then
    echo ""
    echo -e "${CYAN}ZIP files created:${NC}"
    ls -lh "$OUTPUT_BASE"/*.zip 2>/dev/null | awk '{print "  " $NF " (" $5 ")"}' || true
fi

echo ""
if [ ${#FAILED[@]} -gt 0 ]; then
    echo -e "${RED}Done with errors.${NC}"
    exit 1
fi

echo -e "${GREEN}Done!${NC}"
