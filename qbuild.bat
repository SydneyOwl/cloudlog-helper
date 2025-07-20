@echo off
setlocal enabledelayedexpansion

set "tagName=%~1"
if "!tagName!"=="" set "tagName="

for /f "delims=" %%a in ('git rev-parse --short HEAD') do set "commitHash=%%a"

for /f "tokens=1-3 delims=/ " %%a in ('date /t') do (
    set "yyyy=%%a"
    set "mm=%%b"
    set "dd=%%c"
)

for /f "tokens=1-2 delims=: " %%a in ('time /t') do (
    set "hour=%%a"
    set "minute=%%b"
)

set "buildTime=%yyyy%%mm%%dd% %hour%%minute%00"

echo building %tagName% %commitHash% %buildTime%

powershell -Command "(Get-Content Resources/VersionInfo.cs) -replace '@INTERNAL_COMMIT@', '%commitHash%' -replace '@INTERNAL_TIME@', '%buildTime%' -replace '@INTERNAL_VERSION@', '%tagName%' | Set-Content Resources/VersionInfo.cs"

rmdir /s /q bin\Release\net6.0\win-x64\publish
del /q bin\*.zip

dotnet restore -r win-x64
dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true --self-contained true
xcopy /y /q bin\hamlib\winx64\* bin\Release\net6.0\win-x64\publish\
powershell Compress-Archive -Path bin\Release\net6.0\win-x64\publish\* -DestinationPath bin\CloudlogHelper-v%tagName%-windows-x64.zip -Force

dotnet restore -r win-x86
dotnet publish -c Release -r win-x86 /p:PublishSingleFile=true --self-contained true
xcopy /y /q bin\hamlib\winx86\* bin\Release\net6.0\win-x86\publish\
powershell Compress-Archive -Path bin\Release\net6.0\win-x86\publish\* -DestinationPath bin\CloudlogHelper-v%tagName%-windows-x86.zip -Force

dotnet restore -r linux-x64
dotnet publish -c Release -r linux-x64 /p:PublishSingleFile=true --self-contained true
xcopy /y /q bin\hamlib\linuxx64\* bin\Release\net6.0\linux-x64\publish\
powershell Compress-Archive -Path bin\Release\net6.0\linux-x64\publish\* -DestinationPath bin\CloudlogHelper-v%tagName%-linux-x64.zip -Force

@rem Resources/VersionInfo.cs
@rem move Resources/VersionInfo.cs.bak Resources/VersionInfo.cs

endlocal