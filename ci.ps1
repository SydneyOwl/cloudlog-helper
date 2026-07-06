param(
    [string]$Version = "dev_build",
    [string[]]$Platforms = @("win-x64", "win-x86", "win-arm64", "linux-x64", "linux-arm", "linux-arm64", "linux-musl-x64", "osx-x64", "osx-arm64")
)


$buildType = "NORMAL"
$ErrorActionPreference = "Stop"

$commitHash = git rev-parse --short HEAD
$buildTime = Get-Date -Format "yyyyMMdd HHmmss"

Write-Host "Building version=$Version commit=$commitHash time=$buildTime"
Set-Location src\CloudlogHelper
$versionInfoPath = "Resources/VersionInfo.cs"
$versionInfoPathBak = "Resources/VersionInfo.bak"

if (Test-Path $versionInfoPath)
{
    $content = Get-Content $versionInfoPath -Raw
    Copy-Item $versionInfoPath $versionInfoPathBak -Force

    $content = $content -replace '@INTERNAL_COMMIT@', $commitHash `
                          -replace '@INTERNAL_TIME@', $buildTime `
                          -replace '@INTERNAL_VERSION@', $Version `
                          -replace '@INTERNAL_BUILDTYPE@', $buildType

    Set-Content $versionInfoPath -Value $content -NoNewline
}

Remove-Item -Path "bin\Release\*" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "bin\*.zip" -Force -ErrorAction SilentlyContinue

mkdir tmp -Force | Out-Null
mkdir "Resources/Dependencies" -Force | Out-Null
mkdir "Resources/Dependencies/hamlib" -Force | Out-Null

@(
    "win-x86", "win-x64", "linux-x64", "linux-armhf", "linux-arm64", "osx-x64", "osx-arm64"
) | ForEach-Object {
    mkdir "Resources/Dependencies/hamlib/$_" -Force | Out-Null
}

$hamlibReleaseInfo = Invoke-RestMethod -Uri "https://api.github.com/repos/Hamlib/Hamlib/releases/latest"
$latestHamlibVersion = $hamlibReleaseInfo.tag_name
Write-Host "Latest official hamlib version: $latestHamlibVersion" -ForegroundColor Green

$hamlibLinuxReleaseInfo = Invoke-RestMethod -Uri "https://api.github.com/repos/sydneyowl/hamlib-crossbuild/releases/latest"
$latestHamlibLinuxVersion = $hamlibLinuxReleaseInfo.tag_name
Write-Host "Latest linux hamlib version: $latestHamlibLinuxVersion" -ForegroundColor Green

function Safe-ExpandAndCopy
{
    param(
        [string]$Zip,
        [string]$Dest,
        [string[]]$CopyFrom,
        [string]$CopyTo
    )

    if (Test-Path $Zip)
    {
        Expand-Archive -Path $Zip -DestinationPath $Dest -Force
        foreach ($src in $CopyFrom)
        {
            if (Test-Path $src)
            {
                Copy-Item -Path $src -Destination $CopyTo -Force
            }
        }
    }
}

function Need($name)
{
    return $Platforms -contains $name
}

function Remove-DebugSymbols
{
    param(
        [string]$Path
    )

    if (Test-Path $Path)
    {
        Get-ChildItem -Path $Path -Recurse -File -Include "*.pdb", "*.dbg" |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
}

### Windows x86
if (Need "win-x86")
{
    $url = "https://github.com/Hamlib/Hamlib/releases/download/$latestHamlibVersion/hamlib-w32-$latestHamlibVersion.zip"
    Invoke-WebRequest -Uri $url -OutFile "./tmp/w32.zip"
    Safe-ExpandAndCopy -Zip "./tmp/w32.zip" `
                       -Dest "./tmp/w32" `
                       -CopyFrom  @(
        "./tmp/w32/hamlib-w32-$latestHamlibVersion/bin/rigctld.exe",
        "./tmp/w32/hamlib-w32-$latestHamlibVersion/bin/*.dll"
    ) `
                       -CopyTo "./Resources/Dependencies/hamlib/win-x86"
}

### Windows x64
if (Need "win-x64")
{
    $url = "https://github.com/Hamlib/Hamlib/releases/download/$latestHamlibVersion/hamlib-w64-$latestHamlibVersion.zip"
    Invoke-WebRequest -Uri $url -OutFile "./tmp/w64.zip"
    Safe-ExpandAndCopy -Zip "./tmp/w64.zip" `
                       -Dest "./tmp/w64" `
                       -CopyFrom  @(
        "./tmp/w64/hamlib-w64-$latestHamlibVersion/bin/rigctld.exe",
        "./tmp/w64/hamlib-w64-$latestHamlibVersion/bin/*.dll"
    ) `
                       -CopyTo "./Resources/Dependencies/hamlib/win-x64"
}

### Linux x64
if (Need "linux-x64")
{
    $url = "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$latestHamlibLinuxVersion/Hamlib-linux-amd64-$latestHamlibLinuxVersion.zip"
    Invoke-WebRequest -Uri $url -OutFile "./tmp/linux-amd64.zip"
    Safe-ExpandAndCopy -Zip "./tmp/linux-amd64.zip" `
                       -Dest "./tmp/linux-amd64" `
                       -CopyFrom "./tmp/linux-amd64/bin/rigctld" `
                       -CopyTo "./Resources/Dependencies/hamlib/linux-x64"
}

### Linux arm (armhf)
if (Need "linux-arm")
{
    $url = "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$latestHamlibLinuxVersion/Hamlib-linux-armhf-$latestHamlibLinuxVersion.zip"
    Invoke-WebRequest -Uri $url -OutFile "./tmp/linux-armhf.zip"
    Safe-ExpandAndCopy -Zip "./tmp/linux-armhf.zip" `
                       -Dest "./tmp/linux-armhf" `
                       -CopyFrom "./tmp/linux-armhf/bin/rigctld" `
                       -CopyTo "./Resources/Dependencies/hamlib/linux-armhf"
}

### Linux arm64
if (Need "linux-arm64")
{
    $url = "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$latestHamlibLinuxVersion/Hamlib-linux-arm64-$latestHamlibLinuxVersion.zip"
    Invoke-WebRequest -Uri $url -OutFile "./tmp/linux-arm64.zip"
    Safe-ExpandAndCopy -Zip "./tmp/linux-arm64.zip" `
                       -Dest "./tmp/linux-arm64" `
                       -CopyFrom "./tmp/linux-arm64/bin/rigctld" `
                       -CopyTo "./Resources/Dependencies/hamlib/linux-arm64"
}

### macos x64
if (Need "osx-x64")
{
    $url = "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$latestHamlibLinuxVersion/Hamlib-macos-x86_64-$latestHamlibLinuxVersion.zip"
    Invoke-WebRequest -Uri $url -OutFile "./tmp/osx-x64.zip"
    Safe-ExpandAndCopy -Zip "./tmp/osx-x64.zip" `
                       -Dest "./tmp/osx-x64" `
                       -CopyFrom  @(
        "./tmp/osx-x64/bin/rigctld",
        "./tmp/osx-x64/bin/libusb-1.0.0.dylib"
    ) `
                       -CopyTo "./Resources/Dependencies/hamlib/osx-x64"
}

### macos arm64
if (Need "osx-arm64")
{
    $url = "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$latestHamlibLinuxVersion/Hamlib-macos-arm64-$latestHamlibLinuxVersion.zip"
    Invoke-WebRequest -Uri $url -OutFile "./tmp/osx-arm64.zip"
    Safe-ExpandAndCopy -Zip "./tmp/osx-arm64.zip" `
                       -Dest "./tmp/osx-arm64" `
                       -CopyFrom  @(
        "./tmp/osx-arm64/bin/rigctld",
        "./tmp/osx-arm64/bin/libusb-1.0.0.dylib"
    ) `
                       -CopyTo "./Resources/Dependencies/hamlib/osx-arm64"
}

function Build-And-Package-MacOS
{
    param(
        [string]$runtime,
        [string]$archName,
        [string]$frameworkName,
        [string]$exeName
    )

    Write-Host "Building for $runtime ..." -ForegroundColor Cyan

    dotnet publish -c Release -r $runtime `
    -f $frameworkName `
    -t:BundleApp `
    --self-contained true `
    -p:UseAppHost=true
    
    $zipName = "bin/CloudlogHelper-v$Version-$archName.zip"

    $publish_path = "bin/Release/$frameworkName/$runtime/publish"

    Copy-Item Assets/icon.icns $publish_path/CloudlogHelper.app/Contents/Resources
    Remove-DebugSymbols -Path $publish_path
    Compress-Archive -Path $publish_path/CloudlogHelper.app -DestinationPath $zipName -Force
    Write-Host "Created: $zipName"
}

function Build-And-Package
{
    param(
        [string]$runtime,
        [string]$archName,
        [string]$frameworkName,
        [string]$exeName
    )
    
    if ($runtime -like "osx-*") {
        Build-And-Package-MacOS -runtime $runtime `
                          -archName $archName `
                          -frameworkName $frameworkName `
                          -exeName $exeName
        return
    }

    Write-Host "Building for $runtime ..." -ForegroundColor Cyan
    $selfExtractArg = "-p:IncludeNativeLibrariesForSelfExtract=true"
    if ($runtime -like "linux-*") {
        $selfExtractArg = "-p:IncludeAllContentForSelfExtract=true"
    }

    dotnet publish -c Release -r $runtime `
    -f $frameworkName `
    -p:PublishSingleFile=true `
    --self-contained true `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false `
    $selfExtractArg

    $publish_path = "bin/Release/$frameworkName/$runtime/publish"
    Remove-DebugSymbols -Path $publish_path

    $zipName = ""
    if ($Version)
    {
        $zipName = "bin/CloudlogHelper-v$Version-$archName.zip"
    }
    else
    {
        $zipName = "bin/CloudlogHelper-$archName.zip"
    }
    

    $files_to_compress = Get-ChildItem -Path $publish_path
    Compress-Archive -Path $files_to_compress.FullName -DestinationPath $zipName -Force
    Write-Host "Created: $zipName"
}

$PlatformMap = @{
    "win-x64" = @{ arch = "windows-x64"; exe = "CloudlogHelper.exe"; fw = "net8.0-windows10.0.17763.0" }
    "win-x86" = @{ arch = "windows-x86"; exe = "CloudlogHelper.exe"; fw = "net8.0-windows10.0.17763.0" }
    "win-arm64" = @{ arch = "windows-arm64"; exe = "CloudlogHelper.exe"; fw = "net8.0-windows10.0.17763.0" }
    "linux-x64" = @{ arch = "linux-x64"; exe = "CloudlogHelper"; fw = "net8.0" }
    "linux-musl-x64" = @{ arch = "linux-musl-x64"; exe = "CloudlogHelper"; fw = "net8.0" }
    "linux-arm" = @{ arch = "linux-arm"; exe = "CloudlogHelper"; fw = "net8.0" }
    "linux-arm64" = @{ arch = "linux-arm64"; exe = "CloudlogHelper"; fw = "net8.0" }
    "osx-x64" = @{ arch = "osx-x64"; exe = "CloudlogHelper"; fw = "net8.0" }
    "osx-arm64" = @{ arch = "osx-arm64"; exe = "CloudlogHelper"; fw = "net8.0" }
}

foreach ($p in $Platforms)
{
    if ( $PlatformMap.ContainsKey($p))
    {
        $cfg = $PlatformMap[$p]
        Build-And-Package -runtime $p `
                          -archName $cfg.arch `
                          -frameworkName $cfg.fw `
                          -exeName $cfg.exe
    }
    else
    {
        Write-Warning "Unknown platform: $p"
    }
}


if (Test-Path $versionInfoPathBak)
{
    Move-Item $versionInfoPathBak $versionInfoPath -Force
}

Write-Host "--------------------------> Build completed successfully!"
