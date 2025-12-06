#!/bin/bash
set -e 
TAG_NAME=""
TARGET_PLATFORMS=""

while [[ $# -gt 0 ]]; do
    case $1 in
        -t|--tag)
            TAG_NAME="$2"
            shift 2
            ;;
        -p|--platforms)
            TARGET_PLATFORMS="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo "Options:"
            echo "  -t, --tag <version>       Application build version number, default is dev-build"
            echo "  -p, --platforms <list>    Target platforms (comma-separated, e.g., win-x64,linux-x64)"
            echo "                            You can choose from win-x86,win-x64,linux-x64,linux-arm,linux-arm64"
            echo "  -h, --help                Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Get Git commit hash
COMMIT_HASH=$(git rev-parse --short HEAD)

# Get build timestamp
BUILD_TIME=$(date +"%Y%m%d %H%M%S")

echo "Build Parameters:"
echo "  Version: ${TAG_NAME:-Not specified}"
echo "  Target Platforms: ${TARGET_PLATFORMS:-All}"
echo "  Commit Hash: $COMMIT_HASH"
echo "  Build Time: $BUILD_TIME"
echo ""

# Check required commands
check_command() {
    if ! command -v "$1" &> /dev/null; then
        echo "Error: Command '$1' not found, please install first"
        exit 1
    fi
}

check_command git
check_command dotnet
check_command curl
check_command unzip
check_command jq

# Navigate to project directory
cd src/CloudlogHelper || { echo "Error: Directory src/CloudlogHelper not found"; exit 1; }

# Backup and modify version info file
VERSION_INFO_PATH="Resources/VersionInfo.cs"
VERSION_INFO_BAK="Resources/VersionInfo.bak"

if [ ! -f "$VERSION_INFO_PATH" ]; then
    echo "Error: Version info file $VERSION_INFO_PATH not found"
    exit 1
fi

# Backup original file
cp "$VERSION_INFO_PATH" "$VERSION_INFO_BAK"

# Replace version information
if [ -n "$TAG_NAME" ]; then
    sed -i "s/@INTERNAL_VERSION@/$TAG_NAME/g" "$VERSION_INFO_PATH"
else
    sed -i "s/@INTERNAL_VERSION@/dev-build/g" "$VERSION_INFO_PATH"
fi
sed -i "s/@INTERNAL_COMMIT@/$COMMIT_HASH/g" "$VERSION_INFO_PATH"
sed -i "s/@INTERNAL_TIME@/$BUILD_TIME/g" "$VERSION_INFO_PATH"

# Clean previous builds
rm -rf bin/Release/* bin/*.zip 2>/dev/null || true
mkdir -p tmp

# Download and prepare dependencies
echo "Downloading Hamlib dependencies..."

# Get latest versions
HAMLIB_RELEASE_INFO=$(curl -s https://api.github.com/repos/Hamlib/Hamlib/releases/latest)
LATEST_HAMLIB_VERSION=$(echo "$HAMLIB_RELEASE_INFO" | jq -r '.tag_name')
echo "Latest Hamlib official release: $LATEST_HAMLIB_VERSION"

HAMLIB_LINUX_RELEASE_INFO=$(curl -s https://api.github.com/repos/sydneyowl/hamlib-crossbuild/releases/latest)
LATEST_HAMLIB_LINUX_VERSION=$(echo "$HAMLIB_LINUX_RELEASE_INFO" | jq -r '.tag_name')
echo "Latest Hamlib Linux cross-build release: $LATEST_HAMLIB_LINUX_VERSION"

download_and_extract() {
    local url="$1"
    local output_file="$2"
    local extract_dir="$3"
    
    echo "Downloading: $url"
    curl -L -o "$output_file" "$url" || { echo "Download failed: $url"; exit 1; }
    
    if [ ! -f "$output_file" ]; then
        echo "Error: File not downloaded: $output_file"
        exit 1
    fi
    
    echo "Extracting to: $extract_dir"
    mkdir -p "$extract_dir"
    unzip -q "$output_file" -d "$extract_dir" || { echo "Extraction failed: $output_file"; exit 1; }
}

prepare_hamlib() {
    local platform="$1"
    local version="$2"
    local src_dir="$3"
    local dst_dir="$4"
    local is_windows="$5"
    
    mkdir -p "$dst_dir"
    
    if [ "$is_windows" = "true" ]; then
        # Windows platform
        find "$src_dir" -name "*.dll" -exec cp {} "$dst_dir" \;
        cp "$src_dir/rigctld.exe" "$dst_dir" 2>/dev/null || true
    else
        # Linux platform
        cp "$src_dir/rigctld" "$dst_dir" 2>/dev/null || true
    fi
    
    echo "Prepared $platform dependencies:"
    ls -la "$dst_dir"
}

if [ -z "$TARGET_PLATFORMS" ] || [[ "$TARGET_PLATFORMS" == *"win-x86"* ]]; then
    download_and_extract \
        "https://github.com/Hamlib/Hamlib/releases/download/$LATEST_HAMLIB_VERSION/hamlib-w32-$LATEST_HAMLIB_VERSION.zip" \
        "./tmp/hamlib-w32-$LATEST_HAMLIB_VERSION.zip" \
        "./tmp/"
    
    prepare_hamlib "win-x86" "$LATEST_HAMLIB_VERSION" \
        "./tmp/hamlib-w32-$LATEST_HAMLIB_VERSION/bin" \
        "./Resources/Dependencies/hamlib/win-x86" \
        "true"
fi

if [ -z "$TARGET_PLATFORMS" ] || [[ "$TARGET_PLATFORMS" == *"win-x64"* ]]; then
    download_and_extract \
        "https://github.com/Hamlib/Hamlib/releases/download/$LATEST_HAMLIB_VERSION/hamlib-w64-$LATEST_HAMLIB_VERSION.zip" \
        "./tmp/hamlib-w64-$LATEST_HAMLIB_VERSION.zip" \
        "./tmp/"
    
    prepare_hamlib "win-x64" "$LATEST_HAMLIB_VERSION" \
        "./tmp/hamlib-w64-$LATEST_HAMLIB_VERSION/bin" \
        "./Resources/Dependencies/hamlib/win-x64" \
        "true"
fi

if [ -z "$TARGET_PLATFORMS" ] || [[ "$TARGET_PLATFORMS" == *"linux-x64"* ]]; then
    download_and_extract \
        "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$LATEST_HAMLIB_LINUX_VERSION/Hamlib-linux-amd64-$LATEST_HAMLIB_LINUX_VERSION.zip" \
        "./tmp/Hamlib-linux-amd64-$LATEST_HAMLIB_LINUX_VERSION.zip" \
        "./tmp/Hamlib-linux-amd64-$LATEST_HAMLIB_LINUX_VERSION"
    
    prepare_hamlib "linux-x64" "$LATEST_HAMLIB_LINUX_VERSION" \
        "./tmp/Hamlib-linux-amd64-$LATEST_HAMLIB_LINUX_VERSION/bin" \
        "./Resources/Dependencies/hamlib/linux-x64" \
        "false"
fi

if [ -z "$TARGET_PLATFORMS" ] || [[ "$TARGET_PLATFORMS" == *"linux-arm"* ]]; then
    download_and_extract \
        "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$LATEST_HAMLIB_LINUX_VERSION/Hamlib-linux-armhf-$LATEST_HAMLIB_LINUX_VERSION.zip" \
        "./tmp/Hamlib-linux-armhf-$LATEST_HAMLIB_LINUX_VERSION.zip" \
        "./tmp/Hamlib-linux-armhf-$LATEST_HAMLIB_LINUX_VERSION"
    
    prepare_hamlib "linux-armhf" "$LATEST_HAMLIB_LINUX_VERSION" \
        "./tmp/Hamlib-linux-armhf-$LATEST_HAMLIB_LINUX_VERSION/bin" \
        "./Resources/Dependencies/hamlib/linux-armhf" \
        "false"
fi

if [ -z "$TARGET_PLATFORMS" ] || [[ "$TARGET_PLATFORMS" == *"linux-arm64"* ]]; then
    download_and_extract \
        "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$LATEST_HAMLIB_LINUX_VERSION/Hamlib-linux-arm64-$LATEST_HAMLIB_LINUX_VERSION.zip" \
        "./tmp/Hamlib-linux-arm64-$LATEST_HAMLIB_LINUX_VERSION.zip" \
        "./tmp/Hamlib-linux-arm64-$LATEST_HAMLIB_LINUX_VERSION"
    
    prepare_hamlib "linux-arm64" "$LATEST_HAMLIB_LINUX_VERSION" \
        "./tmp/Hamlib-linux-arm64-$LATEST_HAMLIB_LINUX_VERSION/bin" \
        "./Resources/Dependencies/hamlib/linux-arm64" \
        "false"
fi

build_and_package() {
    local runtime="$1"
    local arch_name="$2"
    local framework_name="$3"
    local exe_name="${4:-CloudlogHelper.exe}"
    
    # Check if platform is in target list
    if [ -n "$TARGET_PLATFORMS" ] && [[ ! "$TARGET_PLATFORMS" == *"$runtime"* ]]; then
        echo "Skipping $runtime ($arch_name), not in target platforms list"
        return 0
    fi
    
    if [[ "$framework_name" == *"windows"* ]] ; then
        echo "Warning: Cannot build Windows target '$framework_name' on Linux system!"
        echo "         Windows targets require Windows build environment"
        
        read -p "Do you wish to use fallback runtime (system native notifications and omnirig not supported)? [y/N]: " -n 1 -r
        echo
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            echo "Using fallback runtime: net6.0"
            framework_name="net6.0"
        else
            return 0
        fi
    fi
    
    echo "Building for $runtime ($arch_name)..."
    
    dotnet publish -c Release -r "$runtime" \
        -f "$framework_name" \
        -p:PublishSingleFile=true \
        --self-contained true \
        -p:PublishReadyToRun=false \
        -p:PublishTrimmed=false \
        -p:TrimUnusedDependencies=true \
        -p:IncludeNativeLibrariesForSelfExtract=true
    
    local publish_path="bin/Release/$framework_name/$runtime/publish/$exe_name"
    
    local zip_name
    if [ -n "$TAG_NAME" ]; then
        zip_name="bin/CloudlogHelper-v$TAG_NAME-$arch_name.zip"
    else
        zip_name="bin/CloudlogHelper-$arch_name.zip"
    fi
    
    if [ -f "$publish_path" ]; then
        chmod +x "$publish_path"
        zip -j "$zip_name" "$publish_path"
        echo "Created: $zip_name"
    else
        echo "Warning: Publish file not found: $publish_path"
    fi
}

echo ""
echo "Starting builds..."

if [ -z "$TARGET_PLATFORMS" ]; then
    build_and_package "win-x64" "windows-x64" "net6.0-windows10.0.17763.0"
    build_and_package "win-x86" "windows-x86" "net6.0-windows10.0.17763.0"
    build_and_package "linux-x64" "linux-x64" "net6.0" "CloudlogHelper"
    build_and_package "linux-arm" "linux-arm" "net6.0" "CloudlogHelper"
    build_and_package "linux-arm64" "linux-arm64" "net6.0" "CloudlogHelper"
else
    IFS=',' read -ra PLATFORMS <<< "$TARGET_PLATFORMS"
    for platform in "${PLATFORMS[@]}"; do
        case "$platform" in
            "win-x86")
                build_and_package "win-x86" "windows-x86" "net6.0-windows10.0.17763.0"
                ;;
            "win-x64")
                build_and_package "win-x64" "windows-x64" "net6.0-windows10.0.17763.0"
                ;;
            "linux-x64")
                build_and_package "linux-x64" "linux-x64" "net6.0" "CloudlogHelper"
                ;;
            "linux-arm")
                build_and_package "linux-arm" "linux-arm" "net6.0" "CloudlogHelper"
                ;;
            "linux-arm64")
                build_and_package "linux-arm64" "linux-arm64" "net6.0" "CloudlogHelper"
                ;;
            *)
                echo "Warning: Unknown platform '$platform', skipping"
                ;;
        esac
    done
fi

rm -f "$VERSION_INFO_PATH"
mv "$VERSION_INFO_BAK" "$VERSION_INFO_PATH"

echo ""
echo "Build completed successfully!"
echo "Output files in: src/CloudlogHelper/bin/"
