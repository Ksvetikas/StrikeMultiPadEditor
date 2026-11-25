@echo off
title Strike MultiPad Editor Builder

echo ==========================================
echo   STRIKE MULTIPAD EDITOR - BUILD
echo ==========================================
echo.

where dotnet >nul 2>nul

if errorlevel 1 (
    echo ERROR: .NET SDK was not found.
    echo Install the .NET 10 SDK first.
    pause
    exit /b 1
)

echo Publishing Windows 64-bit application...
echo.

dotnet publish StrikeMultiPadEditor.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o publish

if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    pause
    exit /b 1
)

echo.
echo ==========================================
echo BUILD COMPLETE
echo ==========================================
echo.
echo Your application is in:
echo publish\StrikeMultiPadEditor.exe
echo.

pause