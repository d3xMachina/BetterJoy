@echo off
set "MSBUILD_PATH=%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "options=--nologo -p:PublishProfile=Properties\PublishProfiles\FolderProfile.pubxml"

echo Restore nuget packages...
nuget restore
IF %ERRORLEVEL% NEQ 0 goto :ERROR

echo.
echo Build hidapi 32 bits...
"%MSBUILD_PATH%" "hidapi\windows\hidapi.vcxproj" -p:Configuration=Release -p:Platform=Win32 -t:Build
if %ERRORLEVEL% NEQ 0 goto :ERROR

echo.
echo Build hidapi 64 bits...
"%MSBUILD_PATH%" "hidapi\windows\hidapi.vcxproj" -p:Configuration=Release -p:Platform=x64 -t:Build
if %ERRORLEVEL% NEQ 0 goto :ERROR

echo.
echo Build ViGEm.NET...
"%MSBUILD_PATH%" "ViGEm.NET\ViGEmClient\ViGEmClient.NET.csproj" -p:Configuration=Release -p:Platform=x64 -t:Build
if %ERRORLEVEL% NEQ 0 goto :ERROR

echo.
echo Build WindowsInput...
"%MSBUILD_PATH%" "WindowsInput\WindowsInput\WindowsInput.csproj" -p:Configuration=Release -p:Platform=x64 -t:Build
if %ERRORLEVEL% NEQ 0 goto :ERROR

echo.
echo Publish BetterJoy...
dotnet publish BetterJoy %options%
if %ERRORLEVEL% NEQ 0 goto :ERROR

echo.
echo Build succeeded!
pause
goto :eof

:ERROR
echo.
echo Build failed!
pause

