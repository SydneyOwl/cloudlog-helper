param(
    [string]$tagName = ""
)

$ErrorActionPreference = "Stop"

$commitHash = git rev-parse --short HEAD

$buildTime = Get-Date -Format "yyyyMMdd HHmmss"

Write-Host "building $tagName $commitHash $buildTime"

$versionInfoPath = "Resources/VersionInfo.cs"
$content = Get-Content $versionInfoPath -Raw

$content = $content -replace '@INTERNAL_COMMIT@', $commitHash `
                    -replace '@INTERNAL_TIME@', $buildTime `
                    -replace '@INTERNAL_VERSION@', $tagName

Set-Content $versionInfoPath -Value $content -NoNewline

Remove-Item -Path "bin\Release\net6.0\win-x64\publish" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "bin\*.zip" -Force -ErrorAction SilentlyContinue

mkdir tmp -Force

$hamlibReleaseInfo = Invoke-RestMethod -Uri "https://api.github.com/repos/Hamlib/Hamlib/releases/latest"
$latestHamlibVersion = $hamlibReleaseInfo.tag_name
Write-Host "Latest hamlib official release version: $latestHamlibVersion" -ForegroundColor Green

$hamlibLinuxReleaseInfo = Invoke-RestMethod -Uri "https://api.github.com/repos/sydneyowl/hamlib-crossbuild/releases/latest"
$latestHamlibLinuxVersion = $hamlibLinuxReleaseInfo.tag_name
Write-Host "Latest sydneyowl hamlib linux release version: $latestHamlibLinuxVersion" -ForegroundColor Green

##################################

$downloadUrl = "https://gh-proxy.com/github.com/Hamlib/Hamlib/releases/download/$latestHamlibVersion/hamlib-w32-$latestHamlibVersion.zip"
Invoke-WebRequest -Uri $downloadUrl -OutFile "./tmp/hamlib-w32-$latestHamlibVersion.zip"
Expand-Archive -Path "./tmp/hamlib-w32-$latestHamlibVersion.zip" -DestinationPath "./tmp/"
Copy-Item -Path "./tmp/hamlib-w32-$latestHamlibVersion/bin/*.dll" -Destination "./Resources/Dependencies/hamlib/win-x86"
Copy-Item -Path "./tmp/hamlib-w32-$latestHamlibVersion/bin/rigctld.exe" -Destination "./Resources/Dependencies/hamlib/win-x86"

$downloadUrl = "https://gh-proxy.com/github.com/Hamlib/Hamlib/releases/download/$latestHamlibVersion/hamlib-w64-$latestHamlibVersion.zip"
Invoke-WebRequest -Uri $downloadUrl -OutFile "./tmp/hamlib-w64-$latestHamlibVersion.zip"
Expand-Archive -Path "./tmp/hamlib-w64-$latestHamlibVersion.zip" -DestinationPath "./tmp/"
Copy-Item -Path "./tmp/hamlib-w64-$latestHamlibVersion/bin/*.dll" -Destination "./Resources/Dependencies/hamlib/win-x64"
Copy-Item -Path "./tmp/hamlib-w64-$latestHamlibVersion/bin/rigctld.exe" -Destination "./Resources/Dependencies/hamlib/win-x64"

$downloadUrl = "https://gh-proxy.com/github.com/sydneyowl/hamlib-crossbuild/releases/download/$latestHamlibLinuxVersion/Hamlib-linux-amd64-$latestHamlibLinuxVersion.zip"
Invoke-WebRequest -Uri $downloadUrl -OutFile "./tmp/Hamlib-linux-x64-$latestHamlibLinuxVersion.zip"
Expand-Archive -Path "./tmp/Hamlib-linux-x64-$latestHamlibLinuxVersion.zip" -DestinationPath "./tmp/hamlib-linux-x64"
Copy-Item -Path "./tmp/hamlib-linux-x64/bin/rigctld" -Destination "./Resources/Dependencies/hamlib/linux-x64"

########################


function Build-And-Package
{
    param(
        [string]$runtime,
        [string]$archName,
        [string]$exeName = "CloudlogHelper.exe"
    )

    Write-Host "Building for $runtime ($archName)..."

    dotnet restore -r $runtime
    dotnet publish -c Release -r $runtime `
        -p:PublishSingleFile=true `
        --self-contained true `
        -p:PublishReadyToRun=true `
        -p:PublishTrimmed=true `
        -p:IncludeNativeLibrariesForSelfExtract=true

    $publishPath = "bin/Release/net6.0/$runtime/publish/$exeName"
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

Build-And-Package -runtime "win-x64" -archName "windows-x64"
Build-And-Package -runtime "win-x86" -archName "windows-x86"
Build-And-Package -runtime "linux-x64" -archName "linux-x64" -exeName "CloudlogHelper"

Write-Host "-------------------------->  Build completed successfully!"