#!/bin/bash
set -e 
TAG_NAME="armdev"
TARGET_PLATFORM="linux-arm64"

while [[ $# -gt 0 ]]; do
    case $1 in
        -t|--tag)
            TAG_NAME="${2}(ARM AOT Build)"
            shift 2
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

sudo apt install git curl unzip jq -y

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
    
    mkdir -p "$dst_dir"
    
    cp "$src_dir/rigctld" "$dst_dir" 2>/dev/null || true
    
    echo "Prepared $platform dependencies:"
    ls -la "$dst_dir"
}

download_and_extract \
    "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$LATEST_HAMLIB_LINUX_VERSION/Hamlib-linux-arm64-$LATEST_HAMLIB_LINUX_VERSION.zip" \
    "./tmp/Hamlib-linux-arm64-$LATEST_HAMLIB_LINUX_VERSION.zip" \
    "./tmp/Hamlib-linux-arm64-$LATEST_HAMLIB_LINUX_VERSION"

prepare_hamlib "linux-arm64" "$LATEST_HAMLIB_LINUX_VERSION" \
    "./tmp/Hamlib-linux-arm64-$LATEST_HAMLIB_LINUX_VERSION/bin" \
    "./Resources/Dependencies/hamlib/linux-arm64" \
    "false"

build_and_package() {
    local runtime="$1"
    local arch_name="$2"
    local framework_name="$3"
    local exe_name="${4:-CloudlogHelper.exe}"
    
    echo "Building for $runtime ($arch_name)..."
    
    dotnet publish -c Release -r "$runtime" \
        -f "$framework_name" \
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
        zip -j "$zip_name" "$publish_path"
        echo "Created: $zip_name"
    else
        echo "Warning: Publish file not found: $publish_path"
    fi
}

echo ""
echo "Starting builds..."

build_and_package "linux-arm64" "linux-arm64" "net8.0" "CloudlogHelper"

rm -f "$VERSION_INFO_PATH"
mv "$VERSION_INFO_BAK" "$VERSION_INFO_PATH"

echo ""
echo "Build completed successfully!"
echo "Output files in: src/CloudlogHelper/bin/"
