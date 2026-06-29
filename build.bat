@echo off
setlocal

cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Install .NET 10 SDK from https://dotnet.microsoft.com/download
    exit /b 1
)

call :IsBranchWatchRunning
if errorlevel 1 (
    call :PrintRunningMessage
    exit /b 1
)

set PROJECT=BranchWatch\BranchWatch.csproj
set CONFIG=Release
set RUNTIME=win-x64
set OUTPUT=%~dp0publish\%RUNTIME%

echo Building BranchWatch (%CONFIG%, %RUNTIME%)...
dotnet publish "%PROJECT%" -c %CONFIG% -r %RUNTIME% --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUTPUT%"
if errorlevel 1 (
    echo.
    call :IsBranchWatchRunning
    if errorlevel 1 (
        call :PrintRunningMessage
    ) else (
        echo Build failed.
    )
    exit /b 1
)

echo.
echo Done.
echo EXE: %OUTPUT%\BranchWatch.exe
echo.

endlocal
exit /b 0

:IsBranchWatchRunning
tasklist /FI "IMAGENAME eq BranchWatch.exe" 2>nul | find /I "BranchWatch.exe" >nul
if not errorlevel 1 exit /b 1
exit /b 0

:PrintRunningMessage
echo ERROR: BranchWatch is already running.
echo Exit it from the system tray ^(right-click tray icon - Exit^), then run build.bat again.
exit /b 0
