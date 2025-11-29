#!/bin/bash

# SharpCommander - Cross-platform publish script for Linux/macOS

set -e

# Configuration
PROJECT_PATH="src/SharpCommander.Desktop/SharpCommander.Desktop.csproj"
OUTPUT_BASE="publish"
VERSION="2.0.0"

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
    echo "Usage: $0 [OPTIONS] [PLATFORM]"
    echo ""
    echo "Platforms:"
    echo "  win-x64      Windows x64"
    echo "  win-x86      Windows x86"
    echo "  win-arm64    Windows ARM64"
    echo "  linux-x64    Linux x64"
    echo "  linux-arm64  Linux ARM64"
    echo "  osx-x64      macOS x64 (Intel)"
    echo "  osx-arm64    macOS ARM64 (Apple Silicon)"
    echo "  all          All platforms (default)"
    echo ""
    echo "Options:"
    echo "  --no-zip     Skip creating ZIP archives (ZIPs are created by default)"
    echo "  -a, --aot    Enable AOT compilation"
    echo "  -h, --help   Show this help"
    echo ""
    echo "Examples:"
    echo "  $0 linux-x64"
    echo "  $0 all"
    echo "  $0 osx-arm64 --no-zip"
}

publish_platform() {
    local rid=$1
    local output_dir="$OUTPUT_BASE/$rid"
    
    echo -e "${YELLOW}[Building for $rid...]${NC}"
    
    local publish_args=(
        "publish"
        "$PROJECT_PATH"
        "-c" "Release"
        "-r" "$rid"
        "--self-contained" "true"
        "-p:PublishSingleFile=true"
        "-o" "$output_dir"
    )
    
    if [ "$AOT" = true ]; then
        publish_args+=("-p:PublishAot=true")
        echo -e "  ${CYAN}AOT compilation enabled${NC}"
    fi
    
    if dotnet "${publish_args[@]}"; then
        # Get executable name
        local exe_name="SharpCommander.Desktop"
        if [[ "$rid" == win-* ]]; then
            exe_name="SharpCommander.Desktop.exe"
        fi
        
        local exe_path="$output_dir/$exe_name"
        if [ -f "$exe_path" ]; then
            local size=$(du -h "$exe_path" | cut -f1)
            echo -e "  ${GREEN}SUCCESS: $exe_path ($size)${NC}"
        fi
        
        # Create ZIP if requested
        if [ "$CREATE_ZIP" = true ]; then
            local zip_name="SharpCommander-v$VERSION-$rid.zip"
            local zip_path="$OUTPUT_BASE/$zip_name"
            
            echo -e "  ${CYAN}Creating ZIP: $zip_name${NC}"
            
            (cd "$output_dir" && zip -r "../$zip_name" .)
            
            local zip_size=$(du -h "$zip_path" | cut -f1)
            echo -e "  ${GREEN}ZIP created: $zip_path ($zip_size)${NC}"
        fi
        
        return 0
    else
        echo -e "  ${RED}ERROR: Build failed for $rid${NC}"
        return 1
    fi
}

# Default values
PLATFORM="all"
CREATE_ZIP=true
AOT=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --no-zip)
            CREATE_ZIP=false
            shift
            ;;
        -a|--aot)
            AOT=true
            shift
            ;;
        -h|--help)
            print_usage
            exit 0
            ;;
        win-x64|win-x86|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64|all)
            PLATFORM=$1
            shift
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            print_usage
            exit 1
            ;;
    esac
done

# Main execution
print_header

# Create output directory
mkdir -p "$OUTPUT_BASE"

# Define platforms
if [ "$PLATFORM" = "all" ]; then
    PLATFORMS=("win-x64" "win-x86" "win-arm64" "linux-x64" "linux-arm64" "osx-x64" "osx-arm64")
else
    PLATFORMS=("$PLATFORM")
fi

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
    ls -lh "$OUTPUT_BASE"/*.zip 2>/dev/null | awk '{print "  " $NF " (" $5 ")"}'
fi

echo ""
echo -e "${GREEN}Done!${NC}"
