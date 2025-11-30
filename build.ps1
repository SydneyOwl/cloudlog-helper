param(
    [string]$tagName = ""
)

$ErrorActionPreference = "Stop"

$commitHash = git rev-parse --short HEAD

$buildTime = Get-Date -Format "yyyyMMdd HHmmss"

Write-Host "building $tagName $commitHash $buildTime"

Set-Location src\CloudlogHelper
$versionInfoPath = "Resources/VersionInfo.cs"
$versionInfoPathBak = "Resources/VersionInfo.bak"
$content = Get-Content $versionInfoPath -Raw
Copy-Item $versionInfoPath $versionInfoPathBak

$content = $content -replace '@INTERNAL_COMMIT@', $commitHash `
                     -replace '@INTERNAL_TIME@', $buildTime `
                     -replace '@INTERNAL_VERSION@', $tagName

Set-Content $versionInfoPath -Value $content -NoNewline

Remove-Item -Path "bin\Release\*" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "bin\*.zip" -Force -ErrorAction SilentlyContinue

mkdir tmp -Force

$hamlibReleaseInfo = Invoke-RestMethod -Uri "https://api.github.com/repos/Hamlib/Hamlib/releases/latest"
$latestHamlibVersion = $hamlibReleaseInfo.tag_name
Write-Host "Latest hamlib official release version: $latestHamlibVersion" -ForegroundColor Green

$hamlibLinuxReleaseInfo = Invoke-RestMethod -Uri "https://api.github.com/repos/sydneyowl/hamlib-crossbuild/releases/latest"
$latestHamlibLinuxVersion = $hamlibLinuxReleaseInfo.tag_name
Write-Host "Latest sydneyowl hamlib linux release version: $latestHamlibLinuxVersion" -ForegroundColor Green

##################################

$downloadUrl = "https://github.com/Hamlib/Hamlib/releases/download/$latestHamlibVersion/hamlib-w32-$latestHamlibVersion.zip"
Invoke-WebRequest -Uri $downloadUrl -OutFile "./tmp/hamlib-w32-$latestHamlibVersion.zip"
Expand-Archive -Path "./tmp/hamlib-w32-$latestHamlibVersion.zip" -DestinationPath "./tmp/"
Copy-Item -Path "./tmp/hamlib-w32-$latestHamlibVersion/bin/*.dll" -Destination "./Resources/Dependencies/hamlib/win-x86"
Copy-Item -Path "./tmp/hamlib-w32-$latestHamlibVersion/bin/rigctld.exe" -Destination "./Resources/Dependencies/hamlib/win-x86"
Get-ChildItem -Path "./Resources/Dependencies/hamlib/win-x86"

$downloadUrl = "https://github.com/Hamlib/Hamlib/releases/download/$latestHamlibVersion/hamlib-w64-$latestHamlibVersion.zip"
Invoke-WebRequest -Uri $downloadUrl -OutFile "./tmp/hamlib-w64-$latestHamlibVersion.zip"
Expand-Archive -Path "./tmp/hamlib-w64-$latestHamlibVersion.zip" -DestinationPath "./tmp/"
Copy-Item -Path "./tmp/hamlib-w64-$latestHamlibVersion/bin/*.dll" -Destination "./Resources/Dependencies/hamlib/win-x64"
Copy-Item -Path "./tmp/hamlib-w64-$latestHamlibVersion/bin/rigctld.exe" -Destination "./Resources/Dependencies/hamlib/win-x64"
Get-ChildItem -Path "./Resources/Dependencies/hamlib/win-x64"

$downloadUrl = "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$latestHamlibLinuxVersion/Hamlib-linux-amd64-$latestHamlibLinuxVersion.zip"
Invoke-WebRequest -Uri $downloadUrl -OutFile "./tmp/Hamlib-linux-amd64-$latestHamlibLinuxVersion.zip"
Expand-Archive -Path "./tmp/Hamlib-linux-amd64-$latestHamlibLinuxVersion.zip" -DestinationPath "./tmp/Hamlib-linux-amd64-$latestHamlibLinuxVersion"
Copy-Item -Path "./tmp/Hamlib-linux-amd64-$latestHamlibLinuxVersion/bin/rigctld" -Destination "./Resources/Dependencies/hamlib/linux-x64"
Get-ChildItem -Path "./Resources/Dependencies/hamlib/linux-x64"

$downloadUrl = "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$latestHamlibLinuxVersion/Hamlib-linux-armhf-$latestHamlibLinuxVersion.zip"
Invoke-WebRequest -Uri $downloadUrl -OutFile "./tmp/Hamlib-linux-armhf-$latestHamlibLinuxVersion.zip"
Expand-Archive -Path "./tmp/Hamlib-linux-armhf-$latestHamlibLinuxVersion.zip" -DestinationPath "./tmp/Hamlib-linux-armhf-$latestHamlibLinuxVersion"
Copy-Item -Path "./tmp/Hamlib-linux-armhf-$latestHamlibLinuxVersion/bin/rigctld" -Destination "./Resources/Dependencies/hamlib/linux-armhf"
Get-ChildItem -Path "./Resources/Dependencies/hamlib/linux-armhf"

$downloadUrl = "https://github.com/sydneyowl/hamlib-crossbuild/releases/download/$latestHamlibLinuxVersion/Hamlib-linux-arm64-$latestHamlibLinuxVersion.zip"
Invoke-WebRequest -Uri $downloadUrl -OutFile "./tmp/Hamlib-linux-arm64-$latestHamlibLinuxVersion.zip"
Expand-Archive -Path "./tmp/Hamlib-linux-arm64-$latestHamlibLinuxVersion.zip" -DestinationPath "./tmp/Hamlib-linux-arm64-$latestHamlibLinuxVersion"
Copy-Item -Path "./tmp/Hamlib-linux-arm64-$latestHamlibLinuxVersion/bin/rigctld" -Destination "./Resources/Dependencies/hamlib/linux-arm64"
Get-ChildItem -Path "./Resources/Dependencies/hamlib/linux-arm64"

########################


function Build-And-Package
{
    param(
        [string]$runtime,
        [string]$archName,
        [string]$frameworkName,
        [string]$exeName = "CloudlogHelper.exe"
    )

    Write-Host "Building for $runtime ($archName)..."

    dotnet publish -c Release -r $runtime `
        -f $frameworkName `
        -p:PublishSingleFile=true `
        --self-contained true `
        -p:PublishReadyToRun=false `
        -p:PublishTrimmed=false `
        -p:TrimUnusedDependencies=true `
        -p:IncludeNativeLibrariesForSelfExtract=true
    
    $publishPath = "bin/Release/$frameworkName/$runtime/publish/$exeName"
    $zipName = if ($tagName)
    {
        "bin/CloudlogHelper-v$tagName-$archName.zip"
    }
    else
    {
        "bin/CloudlogHelper-$archName.zip"
    }

    Compress-Archive -Path $publishPath -DestinationPath $zipName -Force
    Write-Host "Created: $zipName"
}

Build-And-Package -runtime "win-x64" -archName "windows-x64" -frameworkName "net6.0-windows10.0.17763.0"
Build-And-Package -runtime "win-x86" -archName "windows-x86" -frameworkName "net6.0-windows10.0.17763.0"
Build-And-Package -runtime "linux-x64" -archName "linux-x64" -exeName "CloudlogHelper" -frameworkName "net6.0"
Build-And-Package -runtime "linux-arm" -archName "linux-arm" -exeName "CloudlogHelper" -frameworkName "net6.0"
Build-And-Package -runtime "linux-arm64" -archName "linux-arm64" -exeName "CloudlogHelper" -frameworkName "net6.0"

Remove-Item $versionInfoPath
Move-Item $versionInfoPathBak $versionInfoPath

Write-Host "-------------------------->  Build completed successfully!"