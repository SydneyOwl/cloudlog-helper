#!/bin/bash


tagName="${1:-}"
commitHash=$(git rev-parse --short HEAD)
buildTime=$(date +"%Y-%m-%d %H:%M:%S")

echo "building ${tagName} ${commitHash} ${buildTime}"

cd src/CloudlogHelper

sed -i.bak \
-e "s/@INTERNAL_COMMIT@/$commitHash/g" \
-e "s/@INTERNAL_TIME@/$buildTime/g" \
-e "s/@INTERNAL_VERSION@/$tagName/g" \
Resources/VersionInfo.cs

rm -rf bin/Release/net6.0/win-x64/publish/* bin/*.zip
# build windows x64
dotnet restore -r win-x64
dotnet publish -c Release -r win-x64 -f net6.0-windows10.0.17763.0 -p:UseAppHost=true -p:PublishSingleFile=true --self-contained true -p:PublishReadyToRun=false -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true
zip -rj bin/CloudlogHelper-v${tagName}-windows-x64.zip bin/Release/net6.0/win-x64/publish/CloudlogHelper.exe


# build windows x86
dotnet restore -r win-x86
dotnet publish -c Release -r win-x86 -f net6.0-windows10.0.17763.0 -p:UseAppHost=true -p:PublishSingleFile=true --self-contained true -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true
zip -rj bin/CloudlogHelper-v${tagName}-windows-x86.zip bin/Release/net6.0/win-x86/publish/CloudlogHelper.exe


# build linux x64
dotnet restore -r linux-x64
dotnet publish -c Release -r linux-x64 -f net6.0 -p:PublishSingleFile=true -p:UseAppHost=true --self-contained true -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true
#cp bin/hamlib/linuxx64/* bin/Release/net6.0/linux-x64/publish/
#chmod +x bin/Release/net6.0/linux-x64/publish/rigctld
#chmod +x bin/Release/net6.0/linux-x64/publish/CloudlogHelper
zip -rj bin/CloudlogHelper-v${tagName}-linux-x64.zip bin/Release/net6.0/linux-x64/publish/CloudlogHelper


# resume file
rm Resources/VersionInfo.cs
mv Resources/VersionInfo.cs.bak Resources/VersionInfo.cs
 