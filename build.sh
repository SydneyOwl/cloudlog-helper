#!/bin/bash
set -e 
TAG_NAME=""
TARGET_PLATFORMS=""
BUILD_TYPE="NORMAL"
AOT_BUILD=false
INSTALL_ARM_CHAIN=false
INSTALL_MUSL_CHAIN=false
INSTALL_UBUNTU_CHAIN=false

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
        --aot)
            AOT_BUILD=true
            shift
            ;;
        --install-ubuntu-chain)
          INSTALL_UBUNTU_CHAIN=true
          shift
          ;;
        --install-arm-chain)
            INSTALL_ARM_CHAIN=true
            shift
            ;;
        --install-musl-chain)
            INSTALL_MUSL_CHAIN=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo "Options:"
            echo "  -t, --tag <version>       Application build version number, default is dev_build"
            echo "  -p, --platforms <list>    Target platforms (comma-separated, e.g., win-x64,linux-x64)"
            echo "                            You can choose from win-x86,win-x64,linux-x64,linux-arm,linux-arm64,linux-musl-x64"
            echo "  --aot                     Build using AOT compilation"
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
echo "  AOT Build: $AOT_BUILD"
echo "  Install Chain: $INSTALL_ARM_CHAIN"
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

# Install Ubuntu build chains if requested
if [ "$INSTALL_UBUNTU_CHAIN" = true ]; then
    echo "Installing MUSL chains..."
    sudo apt update
    sudo apt install git curl unzip jq -y
    sudo apt install -y clang zlib1g-dev
    
    echo "ubuntu chains installed successfully!"
fi

# Install musl chains if requested
if [ "$INSTALL_MUSL_CHAIN" = true ]; then
    echo "Installing MUSL chains..."
    sudo apt update
    sudo apt install git curl unzip jq -y
    sudo apt install -y musl-tools
    
    echo "MUSL chains installed successfully!"
fi

# Install crossbuild chains if requested
if [ "$INSTALL_ARM_CHAIN" = true ]; then
    echo "Installing ARM crossbuild chains..."
    sudo apt update
    sudo apt install git curl unzip jq -y
    
    # armhf cross-compile
    sudo dpkg --add-architecture armhf
    sudo apt-get update
    
    sudo apt-get install -y \
        gcc-arm-linux-gnueabihf \
        g++-arm-linux-gnueabihf \
        binutils-arm-linux-gnueabihf \
        libc6-dev-armhf-cross \
        libc6:armhf \
        libstdc++6:armhf \
        libgcc-s1:armhf \
        libc6-dev:armhf
    
    echo "ARM crossbuild chains installed successfully!"
fi

# Validate AOT build constraints
if [ "$AOT_BUILD" = true ]; then
    if [ -n "$TARGET_PLATFORMS" ]; then
        IFS=',' read -ra PLATFORMS <<< "$TARGET_PLATFORMS"
        for platform in "${PLATFORMS[@]}"; do
            case "$platform" in
                "linux-arm"|"linux-arm64"|"linux-x64"|"linux-musl-x64")
                    BUILD_TYPE="AOT"
                    # Valid AOT platforms
                    ;;
                "win-x86"|"win-x64")
                    echo "Warning: AOT build is not supported for platform '$platform'. Ignoring --aot flag for this platform."
                    AOT_BUILD=false
                    ;;
                *)
                    echo "Warning: Unknown platform '$platform', skipping"
                    ;;
            esac
        done
    fi
fi

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
    sed -i "s/@INTERNAL_VERSION@/dev_build/g" "$VERSION_INFO_PATH"
fi
sed -i "s/@INTERNAL_COMMIT@/$COMMIT_HASH/g" "$VERSION_INFO_PATH"
sed -i "s/@INTERNAL_TIME@/$BUILD_TIME/g" "$VERSION_INFO_PATH"
sed -i "s/@INTERNAL_BUILDTYPE@/$BUILD_TYPE/g" "$VERSION_INFO_PATH"

# Clean previous builds
rm -rf bin/Release/* bin/*.zip 2>/dev/null || tru
rm -rf ./tmp
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
    local is_windows="${5:-false}"
    
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

# Download dependencies based on target platforms
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

if [ -z "$TARGET_PLATFORMS" ] || [[ "$TARGET_PLATFORMS" == *"linux-x64"* ]] || [[ "$TARGET_PLATFORMS" == *"linux-musl-x64"* ]]; then
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
    local is_aot="${5:-false}"
    
    # Check if platform is in target list
    if [ -n "$TARGET_PLATFORMS" ] && [[ ! "$TARGET_PLATFORMS" == *"$runtime"* ]]; then
        echo "Skipping $runtime ($arch_name), not in target platforms list"
        return 0
    fi
    
    if [[ "$framework_name" == *"windows"* ]] && [ "$is_aot" = false ]; then
        echo "Warning: Cannot build Windows target '$framework_name' on Linux system!"
        echo "         Windows targets require Windows build environment"
        echo "Using fallback runtime (system native notifications and omnirig not supported)"
        
        framework_name="net6.0"
    fi
    
    echo "Building for $runtime ($arch_name)..."
    echo "  Framework: $framework_name"
    echo "  AOT: $is_aot"
    
    if [ "$is_aot" = true ]; then
         # AOT build settings
         # Note: IncludeNativeLibrariesForSelfExtract doesn't work for AOT builds
         # See: https://github.com/dotnet/runtime/discussions/117986
         dotnet publish -c Release -r "$runtime" \
             -f "$framework_name" \
             -p:TrimUnusedDependencies=true
             #-p:IncludeNativeLibrariesForSelfExtract=true
    else
         dotnet publish -c Release -r "$runtime" \
            -f "$framework_name" \
            -p:PublishSingleFile=true \
            --self-contained true \
            -p:PublishReadyToRun=false \
            -p:PublishTrimmed=false \
            -p:TrimUnusedDependencies=true \
            -p:IncludeNativeLibrariesForSelfExtract=true
    fi
    
    local publish_path="bin/Release/$framework_name/$runtime/publish"
    rm -f "$publish_path/CloudlogHelper.pdb"
    rm -f "$publish_path/CloudlogHelper.dbg"
    
    local zip_name
    if [ -n "$TAG_NAME" ]; then
        if [ "$is_aot" = true ]; then
            zip_name="bin/CloudlogHelper-v$TAG_NAME-$arch_name-AOT.zip"
        else
            zip_name="bin/CloudlogHelper-v$TAG_NAME-$arch_name.zip"
        fi
    else
        if [ "$is_aot" = true ]; then
            zip_name="bin/CloudlogHelper-$arch_name-AOTs.zip"
        else
            zip_name="bin/CloudlogHelper-$arch_name.zip"
        fi
    fi
    
    if [ -f "$publish_path/CloudlogHelper" ]; then
        chmod +x "$publish_path/CloudlogHelper"
        zip "$zip_name" "$publish_path"/*
        echo "Created: $zip_name"
    else
        echo "Warning: Publish file not found: $publish_path"
    fi
}

echo ""
echo "Starting builds..."
echo "AOT Build: $AOT_BUILD"

if [ -z "$TARGET_PLATFORMS" ]; then
    if [ "$AOT_BUILD" = true ]; then
        echo "Warning: Must specify build platform when using aot build!"
    else
        # Regular builds for all platforms
        build_and_package "win-x64" "windows-x64" "net6.0-windows10.0.17763.0"
        build_and_package "win-x86" "windows-x86" "net6.0-windows10.0.17763.0"
        build_and_package "linux-x64" "linux-x64" "net6.0" "CloudlogHelper"
        build_and_package "linux-musl-x64" "linux-musl-x64" "net6.0" "CloudlogHelper"
        build_and_package "linux-arm" "linux-arm" "net6.0" "CloudlogHelper"
        build_and_package "linux-arm64" "linux-arm64" "net6.0" "CloudlogHelper"
    fi
else
    IFS=',' read -ra PLATFORMS <<< "$TARGET_PLATFORMS"
    for platform in "${PLATFORMS[@]}"; do
        case "$platform" in
            "win-x86")
                if [ "$AOT_BUILD" = false ]; then
                    build_and_package "win-x86" "windows-x86" "net6.0-windows10.0.17763.0"
                else
                    echo "Warning: AOT build not supported for win-x86, skipping"
                fi
                ;;
            "win-x64")
                if [ "$AOT_BUILD" = false ]; then
                    build_and_package "win-x64" "windows-x64" "net6.0-windows10.0.17763.0"
                else
                    echo "Warning: AOT build not supported for win-x64, skipping"
                fi
                ;;
            "linux-x64")
                if [ "$AOT_BUILD" = false ]; then
                    build_and_package "linux-x64" "linux-x64" "net6.0" "CloudlogHelper"
                else
                    build_and_package "linux-x64" "linux-x64" "net10.0" "CloudlogHelper" "true"
                fi
                ;;
            "linux-musl-x64")
                if [ "$AOT_BUILD" = false ]; then
                    build_and_package "linux-musl-x64" "linux-musl-x64" "net6.0" "CloudlogHelper"
                else
                    echo "Warning: AOT build not supported for linux-musl-x64, skipping"
                fi
                ;;
            "linux-arm")
                if [ "$AOT_BUILD" = false ]; then
                    build_and_package "linux-arm" "linux-arm" "net6.0" "CloudlogHelper"
                else
                    build_and_package "linux-arm" "linux-arm" "net10.0" "CloudlogHelper" "true"
                fi
                ;;
            "linux-arm64")
                if [ "$AOT_BUILD" = false ]; then
                    build_and_package "linux-arm64" "linux-arm64" "net6.0" "CloudlogHelper"
                else
                    build_and_package "linux-arm64" "linux-arm64" "net10.0" "CloudlogHelper" "true"
                fi
                ;;
            *)
                echo "Warning: Unknown platform '$platform', skipping"
                ;;
        esac
    done
fi

# Restore original version info file
rm -f "$VERSION_INFO_PATH"
mv "$VERSION_INFO_BAK" "$VERSION_INFO_PATH"

echo ""
echo "Build completed successfully!"
echo "Output files in: src/CloudlogHelper/bin/"